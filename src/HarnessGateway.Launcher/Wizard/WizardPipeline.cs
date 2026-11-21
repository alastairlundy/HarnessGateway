/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Core;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Wizard.Abstractions;
using HarnessGateway.Launcher.Wizard.Models;

namespace HarnessGateway.Launcher.Wizard;

public sealed class WizardPipeline
{
    private readonly IConsoleAbstraction _console;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;
    private readonly SettingsStore _settingsStore;
    private readonly ISecureKeyStore _keyStore;
    private readonly ModelCatalogClient? _catalogClient;

    public WizardPipeline(
        IConsoleAbstraction console,
        IReadOnlyList<IHarnessAdapter> adapters,
        SettingsStore settingsStore,
        ISecureKeyStore keyStore,
        ModelCatalogClient? catalogClient = null)
    {
        _console = console;
        _adapters = adapters;
        _settingsStore = settingsStore;
        _keyStore = keyStore;
        _catalogClient = catalogClient;
    }

    public async Task<WizardOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LauncherSettings settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);

            // Strict default-pointer read: no first-entry fallback.
            // A dangling or absent pointer is user-rectifiable - surface the
            // resolver's message (it names 'config set defaultProvider') and abort;
            // a first run resolves to null and prompts with no prefill.
            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(
                settings,
                providerName: null);

            if (resolution is ProviderResolutionResult.Failed failed)
            {
                // Failed carries a non-null user-facing message - no fallback text.
                await Task.Run(() => _console.WriteError(failed.Message), cancellationToken).ConfigureAwait(false);
                WizardAbortReason abortReason = failed.Failure switch
                {
                    ProviderResolutionFailure.DanglingDefaultProvider => WizardAbortReason.DanglingDefaultProvider,
                    ProviderResolutionFailure.AbsentDefaultProvider => WizardAbortReason.AbsentDefaultProvider,
                    _ => WizardAbortReason.CredentialStoreError,
                };
                return new WizardAborted(abortReason);
            }

            // A first run (EmptySettings) resolves to no provider at all and
            // prompts with no prefill; the provider case carries its entry, and
            // the ad-hoc case is unreachable here - the wizard passes no flag
            // pair, so resolution only ever selects a stored entry.
            Provider? defaultProvider = resolution is ProviderResolutionResult.Resolved
            {
                Launch: LaunchResolution.ProviderCase providerCase,
            }
                ? providerCase.Provider
                : null;

            // Locked step order (harness, provider, key, model), with both
            // protocol mismatch checks inside the loop so each can re-enter
            // the harness step. The provider step runs exactly once and its
            // chosen endpoint/protocol survive a loop-back; the model
            // selection does not - it is discarded and re-collected on the
            // next pass.
            ProtocolKind? protocolToCheck = defaultProvider?.DefaultProtocol;
            bool annotateIncompatible = false;
            (string? endpoint, ProtocolKind protocol, Provider? provider) providerResult =
                (null, ProtocolKind.OpenAiChat, null);
            string? harnessName;
            IHarnessAdapter? adapter;

            while (true)
            {
                (string? selectedHarness, WizardAbortReason? harnessAbort) = await RunHarnessStepAsync(
                    protocolToCheck, annotateIncompatible, cancellationToken).ConfigureAwait(true);
                if (harnessAbort is not null) return new WizardAborted(harnessAbort.Value);
                harnessName = selectedHarness;

                Result<IHarnessAdapter> adapterResult = HarnessLookup.TryResolve(_adapters, harnessName);
                adapter = adapterResult.Success ? adapterResult.Value : null;

                if (providerResult.endpoint is null)
                {
                    providerResult = await RunProviderStepAsync(settings, cancellationToken).ConfigureAwait(true);
                    if (providerResult.endpoint is null) return new WizardAborted(WizardAbortReason.Cancelled);
                }

                if (adapter is not null
                    && !adapter.Metadata.SupportedProtocols.Contains(providerResult.protocol))
                {
                    // Post-endpoint re-verify: the interactive counterpart of
                    // the direct-mode exit-3 pre-check. A dead end aborts before the
                    // refusal so an unsupported protocol can never trap the user in a
                    // re-prompt loop; otherwise mirror the direct-mode reason and loop
                    // back to the harness step only - the endpoint result stands.
                    if (!AnyAdapterSupports(providerResult.protocol))
                    {
                        WriteNoHarnessSupports(providerResult.protocol);
                        return new WizardAborted(WizardAbortReason.NoHarnessSupportsProtocol);
                    }

                    await Task.Run(() => _console.WriteError(BuildIncompatibleMessage(
                        adapter.Metadata.Name,
                        providerResult.protocol,
                        adapter.Metadata.SupportedProtocols)), cancellationToken).ConfigureAwait(false);

                    protocolToCheck = providerResult.protocol;
                    annotateIncompatible = true;
                    continue;
                }

                // Resolve the chosen stored entry (the provider step's pick) before the
                // key and model steps so catalogue HTTP auth and the key step both see
                // the secret, never the at-rest reference. A dangling reference
                // names the saved provider.
                //
                // An "[add new provider]" session resolves to nothing: the fresh
                // endpoint is keyless until the user types a key, and it never
                // inherits the default entry's stored credential or default model -
                // the default entry is not what the user picked, so its allowance to
                // speak for the wizard's launch does not extend to an arbitrary new
                // endpoint. A first run resolves to nothing too (no entries exist).
                Provider? resolvedProvider = providerResult.provider;
                if (resolvedProvider is not null)
                {
                    try
                    {
                        resolvedProvider = await ProviderKeyResolver.ResolveAsync(resolvedProvider, _keyStore, cancellationToken).ConfigureAwait(true);
                    }
                    catch (SecureKeyStoreException ex)
                    {
                        await Task.Run(() => _console.WriteError(ex.Message), cancellationToken).ConfigureAwait(false);
                        return new WizardAborted(WizardAbortReason.CredentialStoreError);
                    }
                }

                Provider effectiveProvider = new()
                {
                    Name = resolvedProvider?.Name ?? "wizard",
                    BaseUri = new Uri(providerResult.endpoint),
                    DefaultProtocol = providerResult.protocol,
                    ApiKey = resolvedProvider?.ApiKey,
                    DefaultModel = resolvedProvider?.DefaultModel,
                };

                string? apiKey = await RunKeyStepAsync(effectiveProvider, cancellationToken).ConfigureAwait(true);

                ModelStep modelStep = new(_console, _catalogClient, effectiveProvider);
                bool requiresModel = adapter?.Metadata.RequiresModel ?? false;
                string? savedDefaultModel = providerResult.provider?.DefaultModel;
                ModelSelection modelSelection = await modelStep.RunAsync(
                    requiresModel,
                    savedDefaultModel,
                    adapter?.Metadata.SupportedProtocols,
                    cancellationToken).ConfigureAwait(true);

                ProtocolKind effectiveProtocol = modelSelection.Protocol ?? providerResult.protocol;

                // Post-model re-verify: the model step can hand back a
                // protocol the chosen harness cannot speak, which the launch
                // flow would refuse with exit 3 only after the wizard had
                // already finished. Write the same incompatible message the
                // other two checks use and re-enter the harness step instead -
                // the model selection is discarded and re-collected on the
                // next pass. The loop-top dead-end guard still aborts with
                // NoHarnessSupportsProtocol when no harness can speak the
                // effective protocol, so a mismatch with no possible pick
                // cannot loop forever.
                if (adapter is not null
                    && !adapter.Metadata.SupportedProtocols.Contains(effectiveProtocol))
                {
                    await Task.Run(() => _console.WriteError(BuildIncompatibleMessage(
                        adapter.Metadata.Name,
                        effectiveProtocol,
                        adapter.Metadata.SupportedProtocols)), cancellationToken).ConfigureAwait(false);

                    protocolToCheck = effectiveProtocol;
                    annotateIncompatible = true;
                    continue;
                }

                // Per-model endpoint override: the picked model's stored
                // endpoint goes into the session endpoint the wizard returns -
                // the launch request's flag pair, which wins wholesale - and a
                // line names it so the swap is visible rather than silent. The
                // wizard stays write-free: the override is read from the
                // chosen provider's stored model list and never persisted, and
                // the provider's endpoint stands when the pick carries none.
                string sessionEndpoint = providerResult.endpoint;
                ModelEntry? pickedEntry = FindStoredModelEntry(providerResult.provider, modelSelection.Model);
                if (pickedEntry?.Endpoint is { } endpointOverride)
                {
                    sessionEndpoint = endpointOverride.ToString();
                    string endpointLine =
                        $"Using endpoint {sessionEndpoint} for model '{pickedEntry.Name}'.";
                    await Task.Run(() => _console.WriteMarkup(endpointLine), cancellationToken)
                        .ConfigureAwait(false);
                }

                return new WizardCompleted(new WizardResult(
                    harnessName!,
                    ProviderName: providerResult.provider?.Name,
                    sessionEndpoint,
                    Protocol: effectiveProtocol,
                    ApiKey: apiKey,
                    Model: modelSelection.Model));
            }
        }
        catch (OperationCanceledException)
        {
            return new WizardAborted(WizardAbortReason.Cancelled);
        }
    }

    private async Task<(string? HarnessName, WizardAbortReason? AbortReason)> RunHarnessStepAsync(
        ProtocolKind? protocolToCheck,
        bool annotateIncompatible,
        CancellationToken cancellationToken)
    {
        if (_adapters.Count == 0)
        {
            await Task.Run(() => _console.WriteError("No agent harnesses found."), cancellationToken).ConfigureAwait(false);

            return (null, WizardAbortReason.NoHarnessInstalled);
        }

        // Dead-end guard: if no harness can satisfy the protocol
        // this flow will enforce, abort instead of re-prompting forever.
        if (protocolToCheck is not null && !AnyAdapterSupports(protocolToCheck.Value))
        {
            WriteNoHarnessSupports(protocolToCheck.Value);
            return (null, WizardAbortReason.NoHarnessSupportsProtocol);
        }

        bool showIncompatible = annotateIncompatible;

        while (true)
        {
            // Probe availability once per loop iteration: the dead-end guard,
            // the choice annotations, and the post-pick refusal all consume
            // the same results instead of each re-spawning the detection
            // process. Re-probing per iteration (not cached across them) lets
            // a freshly installed harness show up without restarting the
            // wizard.
            List<(IHarnessAdapter Adapter, bool IsAvailable)> probed = [];
            foreach (IHarnessAdapter adapter in _adapters)
            {
                probed.Add((adapter,
                    await adapter.IsAvailableAsync(cancellationToken).ConfigureAwait(true)));
            }

            // Dead-end guard: selection is restricted to installed
            // harnesses, so a step that no available adapter can satisfy (no
            // harness installed at all, or none installed that supports the
            // required protocol) has no valid pick - abort with install hints
            // instead of re-prompting forever.
            if (!probed.Any(pair =>
                    pair.IsAvailable
                    && (protocolToCheck is null
                        || pair.Adapter.Metadata.SupportedProtocols
                            .Contains(protocolToCheck.Value))))
            {
                if (protocolToCheck is null)
                {
                    WriteNoHarnessInstalled();
                    return (null, WizardAbortReason.NoHarnessInstalled);
                }
                else
                {
                    WriteNoInstalledSupports(protocolToCheck.Value);
                    return (null, WizardAbortReason.NoHarnessSupportsProtocol);
                }
            }

            List<ConsoleChoice> choices = BuildHarnessChoices(
                probed, protocolToCheck, showIncompatible);

            string pick = await _console.PromptChoiceAsync(
                "Select a harness:", choices, cancellationToken).ConfigureAwait(true);

            // The picker labels each choice with Metadata.Name, so `pick` is a
            // display label rather than an input token. Resolve it by display
            // name, then return the command name: everything downstream (the
            // launch argument, the orchestrator) speaks tokens only.
            IHarnessAdapter? selected = _adapters.FirstOrDefault(a =>
                string.Equals(a.Metadata.Name, pick, StringComparison.OrdinalIgnoreCase));

            // Refuse an unavailable harness at selection - the launch
            // cannot work (direct mode's orchestrator check would refuse it
            // later anyway), so fail here with the install hint rather
            // than after the endpoint, key, and model steps.
            if (selected is not null
                && !probed.First(pair => ReferenceEquals(pair.Adapter, selected)).IsAvailable)
            {
                await Task.Run(() => _console.WriteError(BuildNotInstalledMessage(selected)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (protocolToCheck is null
                || selected is null
                || selected.Metadata.SupportedProtocols.Contains(protocolToCheck.Value))
            {
                // Hand back the command name, not the label the user just saw.
                // The wizard result feeds straight into the launch path, which
                // resolves tokens; returning "GitHub Copilot CLI" there would
                // not resolve once display names stopped being matchable.
                return (selected is null ? pick : selected.Metadata.CommandName, null);
            }

            await Task.Run(() => _console.WriteError(BuildIncompatibleMessage(
                pick, protocolToCheck.Value, selected.Metadata.SupportedProtocols)), cancellationToken).ConfigureAwait(false);
            showIncompatible = true;
        }
    }

    private List<ConsoleChoice> BuildHarnessChoices(
        IReadOnlyList<(IHarnessAdapter Adapter, bool IsAvailable)> probed,
        ProtocolKind? protocolToCheck,
        bool showIncompatible)
    {
        List<ConsoleChoice> choices = [];

        foreach ((IHarnessAdapter adapter, bool isAvailable) in probed)
        {
            string? incompatible = null;
            if (showIncompatible
                && protocolToCheck is not null
                && !adapter.Metadata.SupportedProtocols.Contains(protocolToCheck.Value))
            {
                incompatible = ProviderResolution.ProtocolName(protocolToCheck.Value);
            }

            // Annotations are raw text: the presenter owns the parenthesised
            // rendering, so wrapping here would print "((not installed))".
            List<string> annotations = [];
            if (!isAvailable) annotations.Add("not installed");
            if (incompatible is not null) annotations.Add($"incompatible: {incompatible}");

            // The structured state rides alongside the raw annotation so the
            // presenter can colour the line (whole-line red when unavailable,
            // orange for the incompatible segment) without parsing the text.
            choices.Add(new ConsoleChoice(
                adapter.Metadata.Name,
                annotations.Count > 0 ? string.Join("; ", annotations) : null,
                Unavailable: !isAvailable,
                IncompatibleProtocols: incompatible));
        }

        return choices;
    }

    private bool AnyAdapterSupports(ProtocolKind protocol) =>
        _adapters.Any(adapter => adapter.Metadata.SupportedProtocols.Contains(protocol));

    private void WriteNoHarnessInstalled()
    {
        _console.WriteError(
            "No agent harness is installed on this machine. Install one and re-run:");
        foreach (IHarnessAdapter adapter in _adapters)
        {
            _console.WriteError($"  {adapter.Metadata.Name}: {adapter.Metadata.InstallHint}");
        }
    }

    private void WriteNoInstalledSupports(ProtocolKind protocol)
    {
        _console.WriteError(
            $"No installed agent harness supports the {ProviderResolution.ProtocolName(protocol)} " +
            "protocol. Install one and re-run:");
        foreach (IHarnessAdapter adapter in _adapters.Where(
                     a => a.Metadata.SupportedProtocols.Contains(protocol)))
        {
            _console.WriteError($"  {adapter.Metadata.Name}: {adapter.Metadata.InstallHint}");
        }
    }

    private static string BuildNotInstalledMessage(IHarnessAdapter adapter)
    {
        // Mirrors the direct-mode refusal's substance (name + install hint)
        // and names the detection command so a PATH false negative is
        // diagnosable without reading the source.
        string detection = $" (checked with '{adapter.Metadata.DetectionCommand}')";

        return $"Refusing to launch {adapter.Metadata.Name}: it is not installed on this machine" +
               $"{detection}. {adapter.Metadata.InstallHint}";
    }

    private void WriteNoHarnessSupports(ProtocolKind protocol) =>
        _console.WriteError(
            $"No agent harness supports the {ProviderResolution.ProtocolName(protocol)} protocol.");

    private static string BuildIncompatibleMessage(
        string harnessName,
        ProtocolKind protocol,
        IReadOnlySet<ProtocolKind> supportedProtocols)
    {
        // Mirrors the direct-mode exit-3 refusal (harness name, protocol,
        // supported set) minus the CLI-only advice, which does not apply
        // interactively - so the two reasons cannot drift in substance.
        string supported = string.Join(
            ", ",
            supportedProtocols.Select(ProviderResolution.ProtocolName));

        return $"Refusing to launch {harnessName}: the selected provider's protocol " +
               $"({ProviderResolution.ProtocolName(protocol)}) is not supported by {harnessName}, " +
               $"which supports: {supported}.";
    }

    /// <summary>
    /// The stored entry the model step's pick names, matched with the launch
    /// resolver's case-insensitive rule; null when nothing was picked or the
    /// picked name is absent from the chosen provider's catalog.
    /// </summary>
    private static ModelEntry? FindStoredModelEntry(Provider? provider, string? model) =>
        provider is not null && model is not null
            ? provider.Models.FirstOrDefault(m =>
                string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase))
            : null;

    private async Task<(string? endpoint, ProtocolKind protocol, Provider? provider)> RunProviderStepAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        // No saved providers: walk the creation flow directly - the protocol
        // pick first, then the endpoint it hints at.
        if (settings.Providers.Count == 0)
        {
            (string? ep, ProtocolKind proto) = await RunEndpointStepAsync(cancellationToken).ConfigureAwait(true);
            if (ep is null) return (null, ProtocolKind.OpenAiChat, null);
            return (ep, proto, null);
        }

        List<ConsoleChoice> choices = [];
        foreach (Provider provider in settings.Providers)
        {
            choices.Add(new ConsoleChoice(provider.Name));
        }
        choices.Add(new ConsoleChoice("[add new provider]", "create a session entry"));

        while (true)
        {
            string pick = await _console.PromptChoiceAsync(
                "Select a provider:", choices, cancellationToken).ConfigureAwait(true);

            if (string.Equals(pick, "[add new provider]", StringComparison.OrdinalIgnoreCase))
            {
                // No prefill: a new session entry starts with a blank endpoint.
                // Prefilling the default entry's URL here would let a bare Enter
                // silently re-adopt it, while the launch still runs as an ad-hoc
                // keyless session - the user picked "add new", not the default.
                (string? ep, ProtocolKind proto) = await RunEndpointStepAsync(cancellationToken).ConfigureAwait(true);
                if (ep is null) return (null, ProtocolKind.OpenAiChat, null);
                return (ep, proto, null);
            }

            Provider? chosen = settings.Providers.FirstOrDefault(p =>
                string.Equals(p.Name, pick, StringComparison.OrdinalIgnoreCase));
            if (chosen is null) return (null, ProtocolKind.OpenAiChat, null);

            // A saved entry skips the endpoint step's validation, and its
            // BaseUri round-trips through the settings JSON as
            // RelativeOrAbsolute - so a hand-edited settings file can hold a
            // schemeless value. Refuse it here the same way the endpoint step
            // refuses typed input (error + re-prompt) instead of letting it
            // throw at `new Uri(...)` upstream or reach the launch.
            if (IsHttpEndpoint(chosen.BaseUri.ToString()))
            {
                return (EndpointText.TrimTrailingPathSlash(chosen.BaseUri), chosen.DefaultProtocol, chosen);
            }

            await Task.Run(() => _console.WriteError(
                    $"Invalid endpoint '{chosen.BaseUri}' on saved provider '{chosen.Name}': " +
                    "must be an absolute http:// or https:// URL. Pick another provider or add a new one."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(string? endpoint, ProtocolKind protocol)> RunEndpointStepAsync(
        CancellationToken cancellationToken)
    {
        // Protocol first: a fresh entry has no saved protocol to inherit, and
        // the endpoint prompt's hint depends on the pick - OpenAI-style bases
        // include /v1, Anthropic-style bases omit it (the harness appends
        // /v1/messages itself).
        ProtocolKind protocol = await PromptProtocolAsync(cancellationToken).ConfigureAwait(true);

        // Cancellation and every refusal (empty, schemeless, scheme-invalid)
        // are handled inside the prompt loop: null means the user backed out.
        string? input = await PromptValidEndpointAsync(protocol, cancellationToken).ConfigureAwait(true);
        if (input is null)
        {
            return (null, ProtocolKind.OpenAiChat);
        }

        return (input, protocol);
    }

    private async Task<ProtocolKind> PromptProtocolAsync(CancellationToken cancellationToken)
    {
        List<ConsoleChoice> protocolChoices =
        [
            new("openai-chat"),
            new("openai-responses"),
            new("anthropic"),
        ];

        string protocolStr = await _console.PromptChoiceAsync("Protocol:",
            protocolChoices, cancellationToken).ConfigureAwait(true);

        // The three names above are the only choices offered; an unknown
        // spelling keeps the openai-chat fallback the picker always had.
        return ProviderResolution.TryParseProtocol(protocolStr) ?? ProtocolKind.OpenAiChat;
    }

    /// <summary>
    /// The endpoint prompt's per-protocol hint: where the <c>/v1</c> segment
    /// belongs in the base URL, so the input is guided before the URL is ever
    /// handed to a harness. Plain text only - the presenter markup-parses the
    /// prompt string, so this must never carry style tags.
    /// </summary>
    private static string EndpointHint(ProtocolKind protocol) => protocol switch
    {
        ProtocolKind.Anthropic =>
            "Anthropic-style base URLs omit /v1 (the harness appends it), e.g. https://api.example.com",
        _ =>
            "OpenAI-style endpoints include /v1, e.g. https://api.example.com/v1",
    };

    /// <summary>
    /// Re-prompts for the endpoint URL until a non-empty absolute http(s) URL
    /// is entered, hinting at the <paramref name="protocol"/>'s /v1
    /// convention in the prompt and in every refusal (empty, schemeless, or
    /// non-http(s)) written before each re-prompt. Returns <c>null</c> only on
    /// cancellation.
    /// </summary>
    private async Task<string?> PromptValidEndpointAsync(
        ProtocolKind protocol,
        CancellationToken cancellationToken)
    {
        string hint = EndpointHint(protocol);

        // A null endpoint aborts the whole wizard, and the caller maps that to
        // exit 0 with no output - an empty submission therefore looked like a
        // silent crash. Nothing downstream can default the endpoint, so
        // re-prompt instead; only cancellation still returns null (the user
        // deliberately backing out).
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            string input = await _console.PromptTextAsync(
                $"Endpoint URL ({hint}):",
                cancellationToken: cancellationToken).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(input))
            {
                input = input.Trim();

                // Note: schemeless ("api.example.com/v1") and scheme-invalid
                // ("http://", "ftp://...") entries are refused here, where the
                // user can still retype them, instead of downstream: reaching
                // `new Uri(providerResult.endpoint)` with either means a
                // UriFormatException that escapes RunWizardAsync (its guard
                // catches only I/O and schema faults), while a non-http value
                // that does parse would only surface much later as the vague
                // "Could not fetch model catalogue" (or an
                // InvalidOperationException in the model URI builder).
                if (IsHttpEndpoint(input))
                {
                    return input;
                }

                await Task.Run(() => _console.WriteError(
                        $"Invalid endpoint '{input}': must be an absolute http:// or https:// URL " +
                        $"({hint}). Press Ctrl+C to cancel."),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            await Task.Run(() => _console.WriteError(
                    $"Endpoint URL is required ({hint}). Press Ctrl+C to cancel."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> parses as an absolute <c>http://</c>
    /// or <c>https://</c> URL - the same
    /// <c>Uri.TryCreate(..., UriKind.Absolute, ...)</c> gate every other
    /// endpoint parse in the repo uses, narrowed to the two schemes the
    /// launcher can actually speak. With <c>UriKind.Absolute</c> a schemeless
    /// entry does not parse at all (the one-arg <c>new Uri(string)</c> would
    /// throw a UriFormatException that escapes the wizard), and any other
    /// scheme fails the check, so neither can reach
    /// <c>new Uri(providerResult.endpoint)</c> upstream.
    /// </summary>
    private static bool IsHttpEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
        && parsed is not null
        && (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));

    private async Task<string?> RunKeyStepAsync(
        Provider effectiveProvider,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(effectiveProvider.ApiKey))
        {
            return effectiveProvider.ApiKey;
        }

        bool needsKey = effectiveProvider.DefaultProtocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses =>
                Environment.GetEnvironmentVariable("OPENAI_API_KEY") is null,
            ProtocolKind.Anthropic =>
                Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is null,
            _ => true,
        };

        if (!needsKey)
        {
            return null;
        }

        string key = await _console.PromptTextAsync(
            "API key (will not be saved):",
            secret: true,
            cancellationToken: cancellationToken).ConfigureAwait(true);

        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }
}