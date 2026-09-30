using HarnessGateway.Launcher.Core.Backends.Models;

namespace HarnessGateway.Launcher.Wizard;

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
                catalog = await _fetchModels(_backend, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                await _console.WriteErrorAsync("Could not fetch model catalogue. Endpoint may be unreachable.", cancellationToken).ConfigureAwait(true);
            }
        }

        bool hasCatalog = catalog is { Models.Count: > 0 };

        if (!hasCatalog || catalog is null)
        {
            return await RunDegradedAsync(requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);
        }

        return await RunWithCatalogAsync(catalog,
            requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);
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
            // Raw text - SpectreConsoleAbstraction renders annotations in parens.
            string? annotation = string.Equals(model.Id, savedDefaultModel, StringComparison.OrdinalIgnoreCase)
                ? "saved default"
                : null;
            choices.Add(new ConsoleChoice(model.Id, annotation));
        }

        string title = requiresModel
            ? "Select a model (required):"
            : "Select a model (or skip):";

        string selected = await _console.PromptChoiceAsync(title, choices, cancellationToken).ConfigureAwait(true);

        if (string.Equals(selected, "[skip]", StringComparison.OrdinalIgnoreCase))
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
            await _console.WriteMarkupAsync($"Using saved default model: [green]{savedDefaultModel}[/]", cancellationToken).ConfigureAwait(true);
            return savedDefaultModel;
        }

        if (requiresModel)
        {
            string model = await _console.PromptTextAsync("Enter model name (required):", cancellationToken: cancellationToken).ConfigureAwait(true);
            return string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        }

        string skip = await _console.PromptChoiceAsync("Model catalogue unavailable:", [
            new ConsoleChoice("[skip]", "do not set a model"),
            new ConsoleChoice("[manual]", "enter a model name manually"),
        ], cancellationToken).ConfigureAwait(true);

        if (string.Equals(skip, "[skip]", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string manualModel = await _console.PromptTextAsync("Enter model name:", cancellationToken: cancellationToken).ConfigureAwait(true);
        return string.IsNullOrWhiteSpace(manualModel) ? null : manualModel.Trim();
    }
}
