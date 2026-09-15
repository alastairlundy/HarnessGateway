using LmProxy.AgentLauncher.Commands;
using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Backends.Models;
using LmProxy.AgentLauncher.Core.Settings;

namespace LmProxy.AgentLauncher.Wizard;

public sealed class WizardPipeline
{
    private readonly IConsoleAbstraction _console;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;
    private readonly SettingsStore _settingsStore;
    private readonly ModelCatalogClient? _catalogClient;

    public WizardPipeline(
        IConsoleAbstraction console,
        IReadOnlyList<IHarnessAdapter> adapters,
        SettingsStore settingsStore,
        ModelCatalogClient? catalogClient = null)
    {
        _console = console;
        _adapters = adapters;
        _settingsStore = settingsStore;
        _catalogClient = catalogClient;
    }

    public async Task<WizardResult?> RunAsync(CancellationToken cancellationToken = default)
    {
        LauncherSettings settings = _settingsStore.Load();
        Backend? defaultBackend = ConfigCommand.ResolveDefaultBackend(settings);

        string? harnessName = await RunHarnessStepAsync(cancellationToken);
        if (harnessName is null) return null;

        IHarnessAdapter? adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.Metadata.Name, harnessName, StringComparison.OrdinalIgnoreCase));

        (string? endpoint, ProtocolKind protocol) endpointResult = await RunEndpointStepAsync(defaultBackend, cancellationToken);
        if (endpointResult.endpoint is null) return null;

        Backend effectiveBackend = new()
        {
            Name = "wizard",
            BaseUri = new(endpointResult.endpoint),
            Protocol = endpointResult.protocol,
            ApiKey = defaultBackend?.ApiKey
        };

        string? apiKey = await RunKeyStepAsync(effectiveBackend, defaultBackend, cancellationToken);

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

    private async Task<string?> RunHarnessStepAsync(CancellationToken cancellationToken)
    {
        List<ConsoleChoice> choices = new();

        foreach (IHarnessAdapter adapter in _adapters)
        {
            bool isAvailable = await adapter.IsAvailableAsync(cancellationToken);
            string? annotation = isAvailable ? null : "(not installed)";
            choices.Add(new(adapter.Metadata.Name, annotation));
        }

        if (choices.Count == 0)
        {
            _console.WriteError("No agent harnesses found.");
            return null;
        }

        return await _console.PromptChoiceAsync("Select a harness:", choices, cancellationToken);
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

        List<ConsoleChoice> protocolChoices = new()
        {
            new("openai-chat"),
            new("openai-responses"),
            new("anthropic")
        };

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
        Backend? savedBackend,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(savedBackend?.ApiKey))
        {
            return savedBackend.ApiKey;
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
