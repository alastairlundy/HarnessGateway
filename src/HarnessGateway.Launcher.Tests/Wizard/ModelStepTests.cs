using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Wizard;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class ModelStepTests
{
    private static Provider CreateProvider(
        string endpoint = "https://api.example.com/v1",
        ProtocolKind protocol = ProtocolKind.OpenAiChat)
    {
        return new Provider
        {
            Name = "test",
            BaseUri = new Uri(endpoint),
            DefaultProtocol = protocol
        };
    }

    private static Func<Provider, CancellationToken, Task<ModelCatalog>> CreateCatalogFetcher(ModelCatalog catalog)
    {
        return (_, _) => Task.FromResult(catalog);
    }

    [Test]
    public async Task RunAsync_with_catalog_allows_skip_when_not_required()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("gpt-3.5-turbo")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("[skip]");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(selection.Model).IsNull();
    }

    [Test]
    public async Task RunAsync_with_catalog_blocks_skip_when_required()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("gpt-3.5-turbo")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("gpt-4");

        ModelSelection selection = await step.RunAsync(requiresModel: true, savedDefaultModel: null);

        await Assert.That(selection.Model).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task RunAsync_with_catalog_prefills_saved_default()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();
        ModelCatalog catalog = new ModelCatalog(
        [
            new ModelCatalogEntry("gpt-4"),
            new ModelCatalogEntry("claude-sonnet-5")
        ], DateTimeOffset.UtcNow);

        ModelStep step = new ModelStep(console, CreateCatalogFetcher(catalog), backend);
        console.EnqueueChoice("gpt-4");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: "gpt-4");

        await Assert.That(selection.Model).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task RunAsync_degraded_with_saved_default_returns_saved()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: "saved-model");

        await Assert.That(selection.Model).IsEqualTo("saved-model");
        await Assert.That(console.WrittenMessages.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RunAsync_degraded_requires_model_asks_for_input()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueText("manual-model");

        ModelSelection selection = await step.RunAsync(requiresModel: true, savedDefaultModel: null);

        await Assert.That(selection.Model).IsEqualTo("manual-model");
    }

    [Test]
    public async Task RunAsync_degraded_no_model_required_offers_skip()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[skip]");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(selection.Model).IsNull();
    }

    [Test]
    public async Task RunAsync_degraded_manual_entry_returns_model()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[manual]");
        console.EnqueueText("my-custom-model");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(selection.Model).IsEqualTo("my-custom-model");
    }

    [Test]
    public async Task RunAsync_null_fetchModels_degrades_gracefully()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();

        ModelStep step = new ModelStep(console, fetchModels: null, backend);
        console.EnqueueChoice("[skip]");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(selection.Model).IsNull();
    }

    [Test]
    public async Task RunAsync_fetch_failure_degrades_gracefully()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider();
        Func<Provider, CancellationToken, Task<ModelCatalog>> failingFetcher = (_, _) =>
            throw new HttpRequestException("Connection refused");

        ModelStep step = new ModelStep(console, failingFetcher, backend);
        console.EnqueueChoice("[skip]");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(selection.Model).IsNull();
        await Assert.That(console.ErrorMessages).IsNotEmpty();
    }

    [Test]
    public async Task RunAsync_fetches_once_with_the_provider_default_protocol()
    {
        TestConsole console = new TestConsole();
        Provider backend = CreateProvider(protocol: ProtocolKind.Anthropic);
        int callCount = 0;
        ProtocolKind observedProtocol = ProtocolKind.OpenAiChat;
        Func<Provider, CancellationToken, Task<ModelCatalog>> fetcher = (provider, _) =>
        {
            callCount++;
            observedProtocol = provider.DefaultProtocol;
            return Task.FromResult(new ModelCatalog(
                [new ModelCatalogEntry("claude-sonnet-5")], DateTimeOffset.UtcNow));
        };

        ModelStep step = new ModelStep(console, fetcher, backend);
        console.EnqueueChoice("claude-sonnet-5");

        ModelSelection selection = await step.RunAsync(requiresModel: false, savedDefaultModel: null);

        await Assert.That(callCount).IsEqualTo(1);
        await Assert.That(observedProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(selection.Model).IsEqualTo("claude-sonnet-5");
    }
}
