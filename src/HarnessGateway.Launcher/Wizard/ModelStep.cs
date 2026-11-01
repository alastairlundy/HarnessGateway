using HarnessGateway.Launcher.Core.Providers.Models;

namespace HarnessGateway.Launcher.Wizard;

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

    public async Task<string?> RunAsync(bool requiresModel, string? savedDefaultModel, CancellationToken cancellationToken = default)
    {
        ModelCatalog? catalog = null;

        if (_fetchModels is not null && _provider is not null)
        {
            try
            {
                catalog = await _fetchModels(_provider, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                await Task.Run(() => _console.WriteError("Could not fetch model catalogue. Endpoint may be unreachable."), cancellationToken).ConfigureAwait(false);
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
        List<ConsoleChoice> choices = [];

        if (!requiresModel)
        {
            choices.Add(new ConsoleChoice("[skip]", "do not set a model"));
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
            await Task.Run(() => _console.WriteMarkup($"Using saved default model: [green]{savedDefaultModel}[/]"), cancellationToken).ConfigureAwait(false);
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
