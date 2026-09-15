using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Backends.Models;

namespace LmProxy.AgentLauncher.Wizard;

public sealed class ModelStep
{
    private readonly IConsoleAbstraction _console;
    private readonly Func<Backend, CancellationToken, Task<ModelCatalog>>? _fetchModels;
    private readonly Backend? _backend;

    public ModelStep(IConsoleAbstraction console, Func<Backend, CancellationToken, Task<ModelCatalog>>? fetchModels, Backend? backend)
    {
        _console = console;
        _fetchModels = fetchModels;
        _backend = backend;
    }

    public ModelStep(IConsoleAbstraction console, ModelCatalogClient? catalogClient, Backend? backend)
    {
        _console = console;
        _fetchModels = catalogClient is not null && backend is not null
            ? (b, ct) => catalogClient.FetchModelsAsync(b, ct)
            : null;
        _backend = backend;
    }

    public async Task<string?> RunAsync(bool requiresModel, string? savedDefaultModel, CancellationToken cancellationToken = default)
    {
        ModelCatalog? catalog = null;

        if (_fetchModels is not null && _backend is not null)
        {
            try
            {
                catalog = await _fetchModels(_backend, cancellationToken);
            }
            catch
            {
                _console.WriteError("Could not fetch model catalog. Endpoint may be unreachable.");
            }
        }

        bool hasCatalog = catalog is { Models.Count: > 0 };

        if (!hasCatalog)
        {
            return await RunDegradedAsync(requiresModel, savedDefaultModel, cancellationToken);
        }

        return await RunWithCatalogAsync(catalog!, requiresModel, savedDefaultModel, cancellationToken);
    }

    private async Task<string?> RunWithCatalogAsync(
        ModelCatalog catalog,
        bool requiresModel,
        string? savedDefaultModel,
        CancellationToken cancellationToken)
    {
        List<ConsoleChoice> choices = new();

        if (!requiresModel)
        {
            choices.Add(new("[skip]", "do not set a model"));
        }

        foreach (ModelCatalogEntry model in catalog.Models)
        {
            string? annotation = string.Equals(model.Id, savedDefaultModel, StringComparison.OrdinalIgnoreCase)
                ? "(saved default)"
                : null;
            choices.Add(new(model.Id, annotation));
        }

        string title = requiresModel
            ? "Select a model (required):"
            : "Select a model (or skip):";

        string selected = await _console.PromptChoiceAsync(title, choices, cancellationToken);

        if (selected == "[skip]")
        {
            return null;
        }

        return selected;
    }

    private async Task<string?> RunDegradedAsync(
        bool requiresModel,
        string? savedDefaultModel,
        CancellationToken cancellationToken)
    {
        if (savedDefaultModel is not null)
        {
            _console.WriteMarkup($"Using saved default model: [green]{savedDefaultModel}[/]");
            return savedDefaultModel;
        }

        if (requiresModel)
        {
            string model = await _console.PromptTextAsync("Enter model name (required):", cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        }

        string skip = await _console.PromptChoiceAsync("Model catalogue unavailable:", [
            new("[skip]", "do not set a model"),
            new("[manual]", "enter a model name manually")
        ], cancellationToken);

        if (skip == "[skip]")
        {
            return null;
        }

        string manualModel = await _console.PromptTextAsync("Enter model name:", cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(manualModel) ? null : manualModel.Trim();
    }
}
