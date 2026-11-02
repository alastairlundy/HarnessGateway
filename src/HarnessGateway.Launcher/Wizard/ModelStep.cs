using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;

namespace HarnessGateway.Launcher.Wizard;

/// <summary>
/// The outcome of one <see cref="ModelStep"/> run: the selected model name (or
/// <c>null</c> when skipped/unavailable), the effective protocol for that
/// selection, and the model entries emitted in settings-schema shape.
/// </summary>
public sealed record ModelSelection(
    string? Model,
    ProtocolKind? Protocol,
    IReadOnlyList<ModelEntry> Models);

public sealed class ModelStep
{
    private readonly IConsoleAbstraction _console;
    private readonly Func<Provider, CancellationToken, Task<ModelCatalog>>? _fetchModels;
    private readonly Provider? _provider;

    public ModelStep(IConsoleAbstraction console, Func<Provider, CancellationToken, Task<ModelCatalog>>? fetchModels, Provider? backend)
    {
        _console = console;
        _fetchModels = fetchModels;
        _provider = backend;
    }

    public ModelStep(IConsoleAbstraction console, ModelCatalogClient? catalogClient, Provider? backend)
    {
        _console = console;
        _fetchModels = catalogClient is not null && backend is not null
            ? (b, ct) => catalogClient.FetchModelsAsync(b, ct)
            : null;
        _provider = backend;
    }

    public async Task<ModelSelection> RunAsync(bool requiresModel, string? savedDefaultModel, CancellationToken cancellationToken = default)
    {
        // Fetch the catalog once per protocol kind, merging the results into one
        // listing tagged with every protocol each model was found under.
        Dictionary<string, HashSet<ProtocolKind>> found = new(StringComparer.OrdinalIgnoreCase);

        if (_fetchModels is not null && _provider is not null)
        {
            ProtocolKind[] kinds =
            [
                ProtocolKind.OpenAiChat,
                ProtocolKind.OpenAiResponses,
                ProtocolKind.Anthropic
            ];

            foreach (ProtocolKind kind in kinds)
            {
                try
                {
                    ModelCatalog catalog = await _fetchModels(
                        _provider with { DefaultProtocol = kind }, cancellationToken).ConfigureAwait(true);

                    foreach (ModelCatalogEntry model in catalog.Models)
                    {
                        if (!found.TryGetValue(model.Id, out HashSet<ProtocolKind>? set))
                        {
                            set = new HashSet<ProtocolKind>();
                            found[model.Id] = set;
                        }

                        set.Add(kind);
                    }
                }
                catch
                {
                    await Task.Run(() => _console.WriteError(
                        "Could not fetch model catalogue. Endpoint may be unreachable."),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        bool hasCatalog = found.Count > 0;

        if (!hasCatalog || _provider is null)
        {
            return await RunDegradedAsync(requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);
        }

        return await RunWithCatalogAsync(found, requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);
    }

    private async Task<ModelSelection> RunWithCatalogAsync(
        Dictionary<string, HashSet<ProtocolKind>> found,
        bool requiresModel,
        string? savedDefaultModel,
        CancellationToken cancellationToken)
    {
        List<ConsoleChoice> choices = [];

        if (!requiresModel)
        {
            choices.Add(new ConsoleChoice("[skip]", "do not set a model"));
        }

        foreach (KeyValuePair<string, HashSet<ProtocolKind>> entry in found.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<string> annotationParts = [];

            // Protocols found under, in stable kind order.
            string protocolsText = string.Join(
                ", ",
                entry.Value.OrderBy(p => p).Select(ProviderResolution.ProtocolName));
            annotationParts.Add(protocolsText);

            if (string.Equals(entry.Key, savedDefaultModel, StringComparison.OrdinalIgnoreCase))
            {
                annotationParts.Add("saved default");
            }

            choices.Add(new ConsoleChoice(entry.Key, string.Join("; ", annotationParts)));
        }

        string title = requiresModel
            ? "Select a model (required):"
            : "Select a model (or skip):";

        string selected = await _console.PromptChoiceAsync(title, choices, cancellationToken).ConfigureAwait(true);

        if (string.Equals(selected, "[skip]", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelSelection(null, null, []);
        }

        if (!found.TryGetValue(selected, out HashSet<ProtocolKind>? selectedProtocols) || selectedProtocols is null)
        {
            return new ModelSelection(selected, null, [new ModelEntry { Name = selected }]);
        }

        // Determine the effective protocol: prompt only when more than one
        // protocol was found, preselecting the provider default when present.
        ProtocolKind effectiveProtocol;
        if (selectedProtocols.Count > 1)
        {
            List<ProtocolKind> ordered = selectedProtocols
                .OrderBy(p => p == _provider!.DefaultProtocol ? 0 : 1)
                .ThenBy(p => p)
                .ToList();

            List<ConsoleChoice> protocolChoices = ordered
                .Select(p => new ConsoleChoice(ProviderResolution.ProtocolName(p)))
                .ToList();

            string protocolStr = await _console.PromptChoiceAsync(
                "Protocol:", protocolChoices, cancellationToken).ConfigureAwait(true);

            effectiveProtocol = ParseProtocol(protocolStr) ?? selectedProtocols.First();
        }
        else
        {
            effectiveProtocol = selectedProtocols.First();
        }

        ProtocolKind? overrideProtocol = effectiveProtocol == _provider!.DefaultProtocol
            ? null
            : effectiveProtocol;

        IReadOnlyList<ModelEntry> models = [new ModelEntry { Name = selected, Protocol = overrideProtocol }];

        return new ModelSelection(selected, effectiveProtocol, models);
    }

    private async Task<ModelSelection> RunDegradedAsync(
        bool requiresModel,
        string? savedDefaultModel,
        CancellationToken cancellationToken)
    {
        if (savedDefaultModel is not null)
        {
            await Task.Run(() => _console.WriteMarkup(
                $"Using saved default model: [green]{savedDefaultModel}[/]"), cancellationToken).ConfigureAwait(false);

            return new ModelSelection(
                savedDefaultModel,
                null,
                [new ModelEntry { Name = savedDefaultModel }]);
        }

        if (requiresModel)
        {
            string model = await _console.PromptTextAsync(
                "Enter model name (required):", cancellationToken: cancellationToken).ConfigureAwait(true);
            string? trimmed = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

            return new ModelSelection(
                trimmed,
                null,
                trimmed is null ? [] : [new ModelEntry { Name = trimmed }]);
        }

        string skip = await _console.PromptChoiceAsync("Model catalogue unavailable:", [
            new ConsoleChoice("[skip]", "do not set a model"),
            new ConsoleChoice("[manual]", "enter a model name manually"),
        ], cancellationToken).ConfigureAwait(true);

        if (string.Equals(skip, "[skip]", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelSelection(null, null, []);
        }

        string manualModel = await _console.PromptTextAsync("Enter model name:", cancellationToken: cancellationToken).ConfigureAwait(true);
        string? manual = string.IsNullOrWhiteSpace(manualModel) ? null : manualModel.Trim();

        return new ModelSelection(
            manual,
            null,
            manual is null ? [] : [new ModelEntry { Name = manual }]);
    }

    private static ProtocolKind? ParseProtocol(string value) => value switch
    {
        "openai-chat" => ProtocolKind.OpenAiChat,
        "openai-responses" => ProtocolKind.OpenAiResponses,
        "anthropic" => ProtocolKind.Anthropic,
        _ => null
    };
}