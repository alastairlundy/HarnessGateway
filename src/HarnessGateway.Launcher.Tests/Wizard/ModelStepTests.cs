using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Backends.Models;
using HarnessGateway.Launcher.Wizard;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class ModelStepTests
{
    private static Backend CreateBackend(
        string endpoint = "https://api.example.com/v1",
        ProtocolKind protocol = ProtocolKind.OpenAiChat)
    {
        return new Backend
        {
            Name = "test",
            BaseUri = new Uri(endpoint),
            Protocol = protocol
        };
    }

    private static Func<Backend, CancellationToken, Task<ModelCatalog>> CreateCatalogFetcher(ModelCatalog catalog)
    {
        return (_, _) => Task.FromResult(catalog);
    }

    [Test]
    public async Task RunAsync_with_catalog_allows_skip_when_not_required()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("gpt-3.5-turbo")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("[skip]");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(model).IsNull();
    }

    [Test]
    public async Task RunAsync_with_catalog_blocks_skip_when_required()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("gpt-3.5-turbo")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("gpt-4");

        string? model = await step.RunAsync(requiresModel: true, savedDefaultModel: null);

        await Assert.That(model).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task RunAsync_with_catalog_prefills_saved_default()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("claude-sonnet-5")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("gpt-4");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: "gpt-4");

        await Assert.That(model).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task RunAsync_degraded_with_saved_default_returns_saved()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: "saved-model");

        await Assert.That(model).IsEqualTo("saved-model");
        await Assert.That(console.WrittenMessages.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RunAsync_degraded_requires_model_asks_for_input()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueText("manual-model");

        string? model = await step.RunAsync(requiresModel: true, savedDefaultModel: null);

        await Assert.That(model).IsEqualTo("manual-model");
    }

    [Test]
    public async Task RunAsync_degraded_no_model_required_offers_skip()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[skip]");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(model).IsNull();
    }

    [Test]
    public async Task RunAsync_degraded_manual_entry_returns_model()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[manual]");
        console.EnqueueText("my-custom-model");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(model).IsEqualTo("my-custom-model");
    }

    [Test]
    public async Task RunAsync_null_fetchModels_degrades_gracefully()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[skip]");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(model).IsNull();
    }

    [Test]
    public async Task RunAsync_fetch_failure_degrades_gracefully()
    {
        TestConsole console = new TestConsole();
        Backend backend = CreateBackend();
        Func<Backend, CancellationToken, Task<ModelCatalog>> failingFetcher = (_, _) =>
            throw new HttpRequestException("Connection refused");

        ModelStep step = new ModelStep(console, failingFetcher, backend);
        console.EnqueueChoice("[skip]");

        string? model = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(model).IsNull();
        await Assert.That(console.ErrorMessages).IsNotEmpty();
    }
}
