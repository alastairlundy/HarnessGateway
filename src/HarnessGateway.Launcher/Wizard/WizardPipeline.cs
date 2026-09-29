using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Backends.Models;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;

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

        // Strict default-pointer read (T033/TK006): no first-entry fallback.
        // A dangling or absent pointer is user-rectifiable - surface the
        // resolver's message (it names 'config backend switch') and abort;
        // a first run resolves to null and prompts with no prefill.
        BackendResolutionResult resolution = BackendResolution.Resolve(
            settings,
            backendName: null,
            endpoint: null,
            protocol: null,
            model: null);

        if (!resolution.IsSuccess)
        {
            _console.WriteError(resolution.Message ?? string.Empty);
            return null;
        }

        Backend? defaultBackend = resolution.Backend;

        // T034 locked order (harness, endpoint, key, model). The pairing
        // guard re-enters only the harness step: the endpoint step runs
        // exactly once and its typed endpoint/protocol survive a loop-back.
        ProtocolKind? protocolToCheck = defaultBackend?.Protocol;
        bool annotateIncompatible = false;
        (string? endpoint, ProtocolKind protocol) endpointResult = (null, ProtocolKind.OpenAiChat);
        string? harnessName = null;
        IHarnessAdapter? adapter = null;

        while (true)
        {
            harnessName = await RunHarnessStepAsync(
                protocolToCheck, annotateIncompatible, cancellationToken);
            if (harnessName is null) return null;

            adapter = _adapters.FirstOrDefault(a =>
                string.Equals(a.Metadata.Name, harnessName, StringComparison.OrdinalIgnoreCase));

            if (endpointResult.endpoint is null)
            {
                endpointResult = await RunEndpointStepAsync(defaultBackend, cancellationToken);
                if (endpointResult.endpoint is null) return null;
            }

            if (adapter is null || adapter.Metadata.SupportedProtocols.Contains(endpointResult.protocol))
            {
                break;
            }

            // Post-endpoint re-verify (T028): the interactive counterpart of
            // the direct-mode exit-3 pre-check. A dead end aborts before the
            // refusal so an unsupported protocol can never trap the user in a
            // re-prompt loop; otherwise mirror the direct-mode reason and loop
            // back to the harness step only - the endpoint result stands.
            if (!AnyAdapterSupports(endpointResult.protocol))
            {
                WriteNoHarnessSupports(endpointResult.protocol);
                return null;
            }

            _console.WriteError(BuildIncompatibleMessage(
                harnessName,
                endpointResult.protocol,
                adapter.Metadata.SupportedProtocols));

            protocolToCheck = endpointResult.protocol;
            annotateIncompatible = true;
        }

        // Resolve the saved entry before the key and model steps so catalog HTTP
        // auth and the key step both see the secret, never the at-rest
        // reference - and so a dangling reference names the saved backend.
        Backend? resolvedBackend = defaultBackend;
        if (resolvedBackend is not null)
        {
            try
            {
                resolvedBackend = await BackendKeyResolver.ResolveAsync(resolvedBackend, _keyStore, cancellationToken);
            }
            catch (SecureKeyStoreException ex)
            {
                _console.WriteError(ex.Message);
                return null;
            }
        }

        Backend effectiveBackend = new()
        {
            Name = "wizard",
            BaseUri = new(endpointResult.endpoint),
            Protocol = endpointResult.protocol,
            ApiKey = resolvedBackend?.ApiKey
        };

        string? apiKey = await RunKeyStepAsync(effectiveBackend, cancellationToken);

        ModelStep modelStep = new(_console, _catalogClient, effectiveBackend);
        bool requiresModel = adapter?.Metadata.RequiresModel ?? false;
        string? savedDefaultModel = defaultBackend?.DefaultModel;
        string? model = await modelStep.RunAsync(requiresModel, savedDefaultModel, cancellationToken);

        return new(
            AgentName: harnessName,
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
            _console.WriteError("No agent harnesses found.");
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
            List<ConsoleChoice> choices = await BuildHarnessChoicesAsync(
                protocolToCheck, showIncompatible, cancellationToken);

            string pick = await _console.PromptChoiceAsync(
                "Select a harness:", choices, cancellationToken);

            IHarnessAdapter? selected = _adapters.FirstOrDefault(a =>
                string.Equals(a.Metadata.Name, pick, StringComparison.OrdinalIgnoreCase));

            if (protocolToCheck is null
                || selected is null
                || selected.Metadata.SupportedProtocols.Contains(protocolToCheck.Value))
            {
                return pick;
            }

            _console.WriteError(BuildIncompatibleMessage(
                pick, protocolToCheck.Value, selected.Metadata.SupportedProtocols));
            showIncompatible = true;
        }
    }

    private async Task<List<ConsoleChoice>> BuildHarnessChoicesAsync(
        ProtocolKind? protocolToCheck,
        bool showIncompatible,
        CancellationToken cancellationToken)
    {
        List<ConsoleChoice> choices = [];

        foreach (IHarnessAdapter adapter in _adapters)
        {
            bool isAvailable = await adapter.IsAvailableAsync(cancellationToken);

            string? incompatible = null;
            if (showIncompatible
                && protocolToCheck is not null
                && !adapter.Metadata.SupportedProtocols.Contains(protocolToCheck.Value))
            {
                incompatible = BackendResolution.ProtocolName(protocolToCheck.Value);
            }

            List<string> annotations = [];
            if (!isAvailable) annotations.Add("not installed");
            if (incompatible is not null) annotations.Add($"incompatible: {incompatible}");

            choices.Add(new(
                adapter.Metadata.Name,
                annotations.Count > 0 ? $"({string.Join("; ", annotations)})" : null));
        }

        return choices;
    }

    private bool AnyAdapterSupports(ProtocolKind protocol) =>
        _adapters.Any(adapter => adapter.Metadata.SupportedProtocols.Contains(protocol));

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
        string input = await _console.PromptTextAsync(
            "Endpoint URL:",
            defaultValue: defaultValue,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(input)) return (null, ProtocolKind.OpenAiChat);

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
            new("anthropic")
        ];

        string protocolStr = await _console.PromptChoiceAsync("Protocol:", protocolChoices, cancellationToken);
        ProtocolKind parsedProtocol = protocolStr switch
        {
            "openai-chat" => ProtocolKind.OpenAiChat,
            "openai-responses" => ProtocolKind.OpenAiResponses,
            "anthropic" => ProtocolKind.Anthropic,
            _ => ProtocolKind.OpenAiChat
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
            _ => true
        };

        if (!needsKey)
        {
            return null;
        }

        string key = await _console.PromptTextAsync(
            "API key (will not be saved):",
            secret: true,
            cancellationToken: cancellationToken);

        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }
}
