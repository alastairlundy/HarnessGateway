using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
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

    [Test]
    public async Task RunAsync_unsupported_schema_version_propagates_the_typed_refusal()
    {
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            File.WriteAllText(
                store.SettingsFilePath,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""");

            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

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
        var settings = WizardTestHelper.CreateDefaultSettings(defaultModel: "saved-model");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("typed-key");

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings() with { DefaultBackend = "" };
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings() with { DefaultBackend = "ghost" };
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.anthropic.example.com/v1",
            protocol: ProtocolKind.Anthropic);
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                isAvailable: false,
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            var anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            var pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("https://api.anthropic.example.com/v1");
            console.EnqueueText("anthropic-key");

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings();
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("key-1");

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://saved.example.com/v1",
            protocol: ProtocolKind.OpenAiChat,
            apiKey: "hgkey:default");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            var console = new TestConsole();
            var openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            var anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            var pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueText("https://new.example.com/v1");
            console.EnqueueChoice("anthropic");
            console.EnqueueChoice("AnthropicCapable");

            var result = await pipeline.RunAsync();

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
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            var console = new TestConsole();
            var openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            var anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            var pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueText("https://first-run.example.com/v1");
            console.EnqueueChoice("anthropic");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("first-run-key");

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings(protocol: ProtocolKind.Anthropic);
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            var pipeline = new WizardPipeline(console, [openAiOnly], store, keyStore);

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings();
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter();
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("ordered-key");

            var result = await pipeline.RunAsync();

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
        var settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.anthropic.example.com/v1",
            protocol: ProtocolKind.Anthropic,
            apiKey: "hgkey:default");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            var console = new TestConsole();
            var openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            var anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            var pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("https://api.anthropic.example.com/v1");

            var before = store.Load();
            var result = await pipeline.RunAsync();
            var after = store.Load();

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
        var settings = WizardTestHelper.CreateDefaultSettings(apiKey: "hgkey:default");
        var (tempDir, store, keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            var console = new TestConsole();
            var adapter = WizardTestHelper.CreateTestAdapter("RequiredAgent", requiresModel: true);
            var pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("RequiredAgent");
            console.EnqueueText("https://api.example.com/v1");
            console.EnqueueText("required-model");

            var result = await pipeline.RunAsync();

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
            console.EnqueueChoice("[skip]");

            var result = await pipeline.RunAsync();

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
