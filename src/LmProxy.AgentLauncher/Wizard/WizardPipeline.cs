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
        var settings = _settingsStore.Load();
        var defaultBackend = ConfigCommand.ResolveDefaultBackend(settings);

        var harnessName = await RunHarnessStepAsync(cancellationToken);
        if (harnessName is null) return null;

        var adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.Metadata.Name, harnessName, StringComparison.OrdinalIgnoreCase));

        var endpointResult = await RunEndpointStepAsync(defaultBackend, cancellationToken);
        if (endpointResult.endpoint is null) return null;

        var effectiveBackend = new Backend
        {
            Name = "wizard",
            BaseUri = new Uri(endpointResult.endpoint),
            Protocol = endpointResult.protocol,
            ApiKey = defaultBackend?.ApiKey
        };

        var apiKey = await RunKeyStepAsync(effectiveBackend, defaultBackend, cancellationToken);

        var modelStep = new ModelStep(_console, _catalogClient, effectiveBackend);
        var requiresModel = adapter?.Metadata.RequiresModel ?? false;
        var savedDefaultModel = defaultBackend?.DefaultModel;
        var model = await modelStep.RunAsync(requiresModel, savedDefaultModel, cancellationToken);

        return new WizardResult(
            AgentName: harnessName,
            Endpoint: endpointResult.endpoint!,
            Protocol: endpointResult.protocol,
            ApiKey: apiKey,
            Model: model);
    }

    private async Task<string?> RunHarnessStepAsync(CancellationToken cancellationToken)
    {
        var choices = new List<ConsoleChoice>();

        foreach (var adapter in _adapters)
        {
            var isAvailable = await adapter.IsAvailableAsync(cancellationToken);
            var annotation = isAvailable ? null : "(not installed)";
            choices.Add(new ConsoleChoice(adapter.Metadata.Name, annotation));
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
        var defaultValue = defaultBackend?.BaseUri.ToString().TrimEnd('/');
        var input = await _console.PromptTextAsync(
            "Endpoint URL:",
            defaultValue: defaultValue,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(input)) return (null, ProtocolKind.OpenAiChat);

        input = input.Trim();

        var protocol = defaultBackend?.Protocol ?? ProtocolKind.OpenAiChat;

        if (defaultBackend is not null &&
            string.Equals(input, defaultValue, StringComparison.OrdinalIgnoreCase))
        {
            return (input, protocol);
        }

        var protocolChoices = new List<ConsoleChoice>
        {
            new("openai-chat"),
            new("openai-responses"),
            new("anthropic")
        };

        var protocolStr = await _console.PromptChoiceAsync("Protocol:", protocolChoices, cancellationToken);
        var parsedProtocol = protocolStr switch
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

        var needsKey = effectiveBackend.Protocol switch
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

        var key = await _console.PromptTextAsync(
            "API key (will not be saved):",
            secret: true,
            cancellationToken: cancellationToken);

        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }
}
