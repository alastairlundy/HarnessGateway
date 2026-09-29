using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;
using HarnessGateway.Launcher.Wizard;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class WizardPipelineTests
{
    [Test]
    public async Task RunAsync_harness_step_shows_unavailable_annotated()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter available = WizardTestHelper.CreateTestAdapter("AvailableAgent", isAvailable: true);
            IHarnessAdapter unavailable = WizardTestHelper.CreateTestAdapter("UnavailableAgent", isAvailable: false);
            WizardPipeline pipeline = new WizardPipeline(console, [available, unavailable], store, keyStore);

            console.EnqueueChoice("AvailableAgent");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://saved.example.com/v1",
            protocol: ProtocolKind.OpenAiChat);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://saved.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://old.example.com/v1",
            protocol: ProtocolKind.OpenAiChat);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://new.example.com/v1");
            console.EnqueueChoice("anthropic");

            WizardResult? result = await pipeline.RunAsync();

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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("my-api-key");

            WizardResult? result = await pipeline.RunAsync();

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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

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
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            LauncherSettings savedSettings = store.Load();
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
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            WizardResult? result = await pipeline.RunAsync(cts.Token);

            await Assert.That(result).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_unsupported_schema_version_propagates_the_typed_refusal()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            File.WriteAllText(
                store.SettingsFilePath,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""");

            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            Exception? caught = null;
            try
            {
                await pipeline.RunAsync();
            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("unsupported settings schema version");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_model_default_comes_from_resolved_entry()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(defaultModel: "saved-model");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("typed-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Model).IsEqualTo("saved-model");
            await Assert.That(console.WrittenMessages.Any(m => m.Contains("saved-model"))).IsTrue();
            await Assert.That(
                console.Prompts.Any(p => p.Title == "Model catalogue unavailable:")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_absent_default_pointer_writes_error_and_returns_null()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings() with { DefaultBackend = "" };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("config backend switch");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_dangling_default_pointer_writes_error_and_returns_null()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings() with { DefaultBackend = "ghost" };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("'ghost'");
            await Assert.That(console.ErrorMessages[0]).Contains("config backend switch");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_harness_step_reprompts_with_incompatible_annotations()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.anthropic.example.com/v1",
            protocol: ProtocolKind.Anthropic);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                isAvailable: false,
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("https://api.anthropic.example.com/v1");
            console.EnqueueText("anthropic-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.AgentName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Endpoint).IsEqualTo("https://api.anthropic.example.com/v1");
            await Assert.That(result.ApiKey).IsEqualTo("anthropic-key");

            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
            await Assert.That(
                console.Prompts[0].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("(not installed)");
            await Assert.That(
                console.Prompts[1].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("(not installed; incompatible: anthropic)");
            await Assert.That(
                console.Prompts[1].Choices.Single(c => c.Name == "AnthropicCapable").Annotation)
                .IsNull();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Refusing to launch OpenAiOnly");
            await Assert.That(console.ErrorMessages[0]).Contains("anthropic");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_compatible_pairing_prompts_harness_once()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("key-1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(1);
            await Assert.That(console.ErrorMessages).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_post_endpoint_protocol_change_loops_back_to_harness()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://saved.example.com/v1",
            protocol: ProtocolKind.OpenAiChat,
            apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueText("https://new.example.com/v1");
            console.EnqueueChoice("anthropic");
            console.EnqueueChoice("AnthropicCapable");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.AgentName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Endpoint).IsEqualTo("https://new.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.ApiKey).IsEqualTo("saved-key");

            await Assert.That(console.Prompts.Count).IsEqualTo(5);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Endpoint URL:");
            await Assert.That(console.Prompts[2].Title).IsEqualTo("Protocol:");
            await Assert.That(console.Prompts[3].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[4].Title).IsEqualTo("Model catalogue unavailable:");
            await Assert.That(
                console.Prompts[3].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("(incompatible: anthropic)");
            await Assert.That(console.ErrorMessages.Count).IsEqualTo(1);
            await Assert.That(console.ErrorMessages[0]).Contains("Refusing to launch OpenAiOnly");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_first_run_post_endpoint_check_loops_back()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueText("https://first-run.example.com/v1");
            console.EnqueueChoice("anthropic");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("first-run-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.AgentName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.ApiKey).IsEqualTo("first-run-key");

            // No default backend: nothing to check at the harness step, but
            // the post-endpoint check still fires and loops back.
            await Assert.That(console.Prompts.Count).IsEqualTo(6);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Endpoint URL:");
            await Assert.That(console.Prompts[2].Title).IsEqualTo("Protocol:");
            await Assert.That(console.Prompts[3].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[4].Title).IsEqualTo("API key (will not be saved):");
            await Assert.That(console.Prompts[5].Title).IsEqualTo("Model catalogue unavailable:");
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("anthropic");
            await Assert.That(
                console.Prompts[3].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("(incompatible: anthropic)");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_dead_end_protocol_aborts_without_prompt()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(protocol: ProtocolKind.Anthropic);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("No agent harness supports");
            await Assert.That(console.ErrorMessages[0]).Contains("anthropic");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_step_order_is_harness_endpoint_key_model()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("ordered-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(console.Prompts.Count).IsEqualTo(4);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Endpoint URL:");
            await Assert.That(console.Prompts[2].Title).IsEqualTo("API key (will not be saved):");
            await Assert.That(console.Prompts[3].Title).IsEqualTo("Model catalogue unavailable:");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_guard_path_writes_no_settings()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.anthropic.example.com/v1",
            protocol: ProtocolKind.Anthropic,
            apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("https://api.anthropic.example.com/v1");

            LauncherSettings before = store.Load();
            WizardResult? result = await pipeline.RunAsync();
            LauncherSettings after = store.Load();

            await Assert.That(result).IsNotNull();
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
            await Assert.That(after.Backends.Count).IsEqualTo(before.Backends.Count);
            await Assert.That(after.Backends[0].Name).IsEqualTo(before.Backends[0].Name);
            await Assert.That(after.DefaultBackend).IsEqualTo(before.DefaultBackend);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_model_step_requires_input_when_harness_requires_model()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter("RequiredAgent", requiresModel: true);
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("RequiredAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("required-model");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Model).IsEqualTo("required-model");
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Enter model name (required):")).IsEqualTo(1);
            // Skip is never offered when the harness requires a model.
            await Assert.That(
                console.Prompts.Any(p => p.Title == "Model catalogue unavailable:")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_model_step_allows_skip_when_not_required()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueChoice("[skip]");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Model).IsNull();
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Model catalogue unavailable:")).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
