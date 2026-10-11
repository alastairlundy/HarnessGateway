using HarnessGateway.Launcher.Core;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Backends.Models;

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

    public async Task<WizardResult?> RunAsync(CancellationToken cancellationToken = default)
    {
        LauncherSettings settings = _settingsStore.Load();

        // Strict default-pointer read: no first-entry fallback.
        // A dangling or absent pointer is user-rectifiable - surface the
        // resolver's message (it names 'config set defaultBackend') and abort;
        // a first run resolves to null and prompts with no prefill.
        BackendResolutionResult resolution = BackendResolution.Resolve(
            settings,
            backendName: null,
            endpoint: null,
            protocol: null,
            model: null);

        if (resolution is BackendResolutionResult.Failed failed)
        {
            // Failed carries a non-null user-facing message - no fallback text.
            await Task.Run(() => _console.WriteError(failed.Message), cancellationToken).ConfigureAwait(false);
            return null;
        }

        // A first run (EmptySettings) resolves to no backend at all and
        // prompts with no prefill; Resolved carries its backend non-null.
        Backend? defaultBackend = resolution is BackendResolutionResult.Resolved resolved
            ? resolved.Backend
            : null;

        // Locked step order (harness, endpoint, key, model). The pairing
        // guard re-enters only the harness step: the endpoint step runs
        // exactly once and its typed endpoint/protocol survive a loop-back.
        ProtocolKind? protocolToCheck = defaultBackend?.Protocol;
        bool annotateIncompatible = false;
        (string? endpoint, ProtocolKind protocol) endpointResult = (null, ProtocolKind.OpenAiChat);
        string? harnessName;
        IHarnessAdapter? adapter;

        while (true)
        {
            harnessName = await RunHarnessStepAsync(
                protocolToCheck, annotateIncompatible, cancellationToken).ConfigureAwait(true);
            if (harnessName is null) return null;

            Result<IHarnessAdapter> adapterResult = HarnessLookup.TryResolve(_adapters, harnessName);
            adapter = adapterResult.Success ? adapterResult.Value : null;

            if (endpointResult.endpoint is null)
            {
                endpointResult = await RunEndpointStepAsync(defaultBackend, cancellationToken).ConfigureAwait(true);
                if (endpointResult.endpoint is null) return null;
            }

            if (adapter is null || adapter.Metadata.SupportedProtocols.Contains(endpointResult.protocol))
            {
                break;
            }

            // Post-endpoint re-verify: the interactive counterpart of
            // the direct-mode exit-3 pre-check. A dead end aborts before the
            // refusal so an unsupported protocol can never trap the user in a
            // re-prompt loop; otherwise mirror the direct-mode reason and loop
            // back to the harness step only - the endpoint result stands.
            if (!AnyAdapterSupports(endpointResult.protocol))
            {
                WriteNoHarnessSupports(endpointResult.protocol);
                return null;
            }

            await Task.Run(() => _console.WriteError(BuildIncompatibleMessage(
                adapter.Metadata.Name,
                endpointResult.protocol,
                adapter.Metadata.SupportedProtocols)), cancellationToken).ConfigureAwait(false);
            
            protocolToCheck = endpointResult.protocol;
            annotateIncompatible = true;
        }

        // Resolve the saved entry before the key and model steps so catalogue HTTP
        // auth and the key step both see the secret, never the at-rest
        // reference - and so a dangling reference names the saved backend.
        Backend? resolvedBackend = defaultBackend;
        if (resolvedBackend is not null)
        {
            try
            {
                resolvedBackend = await BackendKeyResolver.ResolveAsync(resolvedBackend, _keyStore, cancellationToken).ConfigureAwait(true);
            }
            catch (SecureKeyStoreException ex)
            {
                await Task.Run(() => _console.WriteError(ex.Message), cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        Backend effectiveBackend = new()
        {
            Name = "wizard",
            BaseUri = new Uri(endpointResult.endpoint),
            Protocol = endpointResult.protocol,
            ApiKey = resolvedBackend?.ApiKey,
        };

        string? apiKey = await RunKeyStepAsync(effectiveBackend, cancellationToken).ConfigureAwait(true);

        ModelStep modelStep = new(_console, _catalogClient, effectiveBackend);
        bool requiresModel = adapter?.Metadata.RequiresModel ?? false;
        string? savedDefaultModel = defaultBackend?.DefaultModel;
        string? model = await modelStep.RunAsync(requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);

        return new WizardResult(
            AgentCommandName: harnessName,
            Endpoint: endpointResult.endpoint!,
            Protocol: endpointResult.protocol,
            ApiKey: apiKey,
            Model: model);
    }

    private async Task<string?> RunHarnessStepAsync(
        ProtocolKind? protocolToCheck,
        bool annotateIncompatible,
        CancellationToken cancellationToken)
    {
        if (_adapters.Count == 0)
        {
            await Task.Run(() => _console.WriteError("No agent harnesses found."), cancellationToken).ConfigureAwait(false);
 
            return null;
        }

        // Dead-end guard (decision 4): if no harness can satisfy the protocol
        // this flow will enforce, abort instead of re-prompting forever.
        if (protocolToCheck is not null && !AnyAdapterSupports(protocolToCheck.Value))
        {
            WriteNoHarnessSupports(protocolToCheck.Value);
            return null;
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
                }
                else
                {
                    WriteNoInstalledSupports(protocolToCheck.Value);
                }

                return null;
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
                return selected is null ? pick : selected.Metadata.CommandName;
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
                incompatible = BackendResolution.ProtocolName(protocolToCheck.Value);
            }

            // Annotations are raw text: the presenter owns the parenthesised
            // rendering, so wrapping here would print "((not installed))".
            List<string> annotations = [];
            if (!isAvailable) annotations.Add("not installed");
            if (incompatible is not null) annotations.Add($"incompatible: {incompatible}");

            choices.Add(new ConsoleChoice(
                adapter.Metadata.Name,
                annotations.Count > 0 ? string.Join("; ", annotations) : null));
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
            $"No installed agent harness supports the {BackendResolution.ProtocolName(protocol)} " +
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
            $"No agent harness supports the {BackendResolution.ProtocolName(protocol)} protocol.");

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
            supportedProtocols.Select(BackendResolution.ProtocolName));

        return $"Refusing to launch {harnessName}: the selected backend's protocol " +
               $"({BackendResolution.ProtocolName(protocol)}) is not supported by {harnessName}, " +
               $"which supports: {supported}.";
    }

    private async Task<(string? endpoint, ProtocolKind protocol)> RunEndpointStepAsync(
        Backend? defaultBackend,
        CancellationToken cancellationToken)
    {
        string? defaultValue = defaultBackend?.BaseUri.ToString().TrimEnd('/');

        // A null endpoint aborts the whole wizard, and the caller maps that to
        // exit 0 with no output - an empty submission therefore looked like a
        // silent crash. Nothing downstream can default the endpoint, so
        // re-prompt instead; only cancellation still returns null (the user
        // deliberately backing out).
        string input;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (null, ProtocolKind.OpenAiChat);
            }

            input = await _console.PromptTextAsync(
                "Endpoint URL:",
                defaultValue: defaultValue,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(input)) 
                break;

            await Task.Run(() => _console.WriteError("Endpoint URL is required, e.g. https://api.example.com/v1. " +
                                                     "Press Ctrl+C to cancel."), cancellationToken).ConfigureAwait(false);
        }

        input = input.Trim();

        ProtocolKind protocol = defaultBackend?.Protocol ?? ProtocolKind.OpenAiChat;

        if (defaultBackend is not null &&
            string.Equals(input, defaultValue, StringComparison.OrdinalIgnoreCase))
        {
            return (input, protocol);
        }

        List<ConsoleChoice> protocolChoices =
        [
            new("openai-chat"),
            new("openai-responses"),
            new("anthropic"),
        ];

        string protocolStr = await _console.PromptChoiceAsync("Protocol:", 
            protocolChoices, cancellationToken).ConfigureAwait(true);
        ProtocolKind parsedProtocol = protocolStr switch
        {
            "openai-chat" => ProtocolKind.OpenAiChat,
            "openai-responses" => ProtocolKind.OpenAiResponses,
            "anthropic" => ProtocolKind.Anthropic,
            _ => ProtocolKind.OpenAiChat,
        };

        return (input, parsedProtocol);
    }

    private async Task<string?> RunKeyStepAsync(
        Backend effectiveBackend,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(effectiveBackend.ApiKey))
        {
            return effectiveBackend.ApiKey;
        }

        bool needsKey = effectiveBackend.Protocol switch
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