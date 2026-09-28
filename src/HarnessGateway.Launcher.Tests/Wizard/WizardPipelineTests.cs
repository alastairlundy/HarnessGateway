using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Wizard;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class WizardPipelineTests
{
    [Test]
    public async Task RunAsync_harness_step_shows_unavailable_annotated()
    {
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            var console = new TestConsole();
            var available = WizardTestHelper.CreateTestAdapter("AvailableAgent", isAvailable: true);
            var unavailable = WizardTestHelper.CreateTestAdapter("UnavailableAgent", isAvailable: false);
            var pipeline = new WizardPipeline(console, [available, unavailable], store, keyStore);

            console.EnqueueChoice("AvailableAgent");
            console.EnqueueText("https://api.example.com/v1");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.AgentName).IsEqualTo("AvailableAgent");
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_endpoint_step_prefills_from_saved_default()
    {
        var settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://saved.example.com/v1",
            protocol: ProtocolKind.OpenAiChat);
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://saved.example.com/v1");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Endpoint).IsEqualTo("https://saved.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_endpoint_step_asks_protocol_when_changed()
    {
        var settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://old.example.com/v1",
            protocol: ProtocolKind.OpenAiChat);
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://new.example.com/v1");
            console.EnqueueChoice("anthropic");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Endpoint).IsEqualTo("https://new.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_key_step_asks_when_no_saved_key_and_no_env_var()
    {
        var settings = WizardTestHelper.CreateDefaultSettings();
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("my-api-key");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.ApiKey).IsEqualTo("my-api-key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_key_step_uses_saved_key()
    {
        var settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.ApiKey).IsEqualTo("saved-key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_dangling_reference_writes_an_error_and_returns_null()
    {
        var settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            var result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("'default'");
            await Assert.That(console.ErrorMessages[0]).Contains("config set-saved-key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_no_settings_written_by_wizard()
    {
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            var result = await pipeline.RunAsync();

            var savedSettings = store.Load();
            await Assert.That(savedSettings.Backends).IsEmpty();
            await Assert.That(savedSettings.DefaultBackend).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_canceled_returns_null()
    {
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await pipeline.RunAsync(cts.Token);

            await Assert.That(result).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
