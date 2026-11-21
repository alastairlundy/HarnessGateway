using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;
using HarnessGateway.Launcher.Wizard;
using HarnessGateway.Launcher.Wizard.Models;

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
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AvailableAgent");
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_refuses_unavailable_harness_and_reprompts()
    {
        // Selecting a harness that is not installed must not walk the
        // endpoint/key/model steps towards the orchestrator's exit-4 refusal.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter unavailable = WizardTestHelper.CreateTestAdapter("UnavailableAgent", isAvailable: false);
            IHarnessAdapter available = WizardTestHelper.CreateTestAdapter("AvailableAgent");
            WizardPipeline pipeline = new WizardPipeline(console, [unavailable, available], store, keyStore);

            console.EnqueueChoice("UnavailableAgent");
            console.EnqueueChoice("AvailableAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AvailableAgent");
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
            // The refusal comes first and names the install hint; no endpoint
            // prompt may happen before the re-prompted harness selection.
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Refusing to launch UnavailableAgent");
            await Assert.That(console.ErrorMessages[0]).Contains("not installed");
            await Assert.That(console.ErrorMessages[0]).Contains("Install UnavailableAgent");
            await Assert.That(
                console.Prompts.TakeWhile(p => p.Title == "Select a harness:").Count()).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_no_installed_harness_aborts_with_install_hints()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter first = WizardTestHelper.CreateTestAdapter("AgentA", isAvailable: false);
            IHarnessAdapter second = WizardTestHelper.CreateTestAdapter("AgentB", isAvailable: false);
            WizardPipeline pipeline = new WizardPipeline(console, [first, second], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("No agent harness is installed");
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("Install AgentA"))).IsTrue();
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("Install AgentB"))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_only_uninstalled_harness_supports_protocol_aborts()
    {
        // Without this guard the harness step would re-prompt forever: the
        // only protocol-capable harness is refused as unavailable, and the
        // only available harness is refused as incompatible.
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(protocol: ProtocolKind.Anthropic);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter(
                "AnthropicCapable",
                isAvailable: false,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.Anthropic });
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0])
                .Contains("No installed agent harness supports the anthropic protocol");
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("Install AnthropicCapable")))
                .IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_saved_default_pick_keeps_endpoint_and_protocol()
    {
        // Accepting the default entry at the provider picker hands its saved
        // endpoint and protocol straight through, with no endpoint step.
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
            await Assert.That(result.Endpoint).IsEqualTo("https://saved.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_add_new_provider_asks_protocol_before_endpoint()
    {
        // The creation flow asks the protocol first, so the endpoint prompt
        // can carry the matching /v1 hint for the chosen protocol.
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
            console.EnqueueChoice("[add new provider]");
            console.EnqueueChoice("anthropic");
            console.EnqueueText("https://new.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://new.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);

            List<PromptLogEntry> prompts = [.. console.Prompts];
            int protocolIndex = prompts.FindIndex(p => p.Title == "Protocol:");
            int endpointIndex = prompts.FindIndex(
                p => p.Title.StartsWith("Endpoint URL", StringComparison.Ordinal));
            await Assert.That(protocolIndex).IsGreaterThan(-1);
            await Assert.That(endpointIndex).IsGreaterThan(protocolIndex);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_empty_endpoint_reprompts_instead_of_aborting()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
            await Assert.That(console.Prompts.Count(
                p => p.Title.StartsWith("Endpoint URL", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Endpoint URL is required");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_schemeless_endpoint_reprompts_with_error()
    {
        // A schemeless entry would otherwise parse as a relative Uri and only
        // surface much later as a vague catalogue-fetch failure; it is refused
        // at the prompt instead, with a re-prompt.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("api.example.com/v1");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
            await Assert.That(console.Prompts.Count(
                p => p.Title.StartsWith("Endpoint URL", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Invalid endpoint 'api.example.com/v1'");
            await Assert.That(console.ErrorMessages[0]).Contains("absolute http:// or https:// URL");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_scheme_invalid_endpoint_reprompts_with_error()
    {
        // "http://" throws UriFormatException on `new Uri(string)`, which the
        // wizard guard in Program.cs does not catch - it must be refused at
        // the prompt instead of escaping RunWizardAsync.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("http://");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
            await Assert.That(console.Prompts.Count(
                p => p.Title.StartsWith("Endpoint URL", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Invalid endpoint 'http://'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_saved_provider_with_relative_endpoint_is_refused_and_reprompted()
    {
        // A saved entry skips the endpoint step's validation, and its BaseUri
        // round-trips through the settings JSON as RelativeOrAbsolute - so a
        // hand-edited settings file can hold a schemeless value. The provider
        // step is the last gate before `new Uri(...)`: it must refuse the
        // entry (error + re-prompt) so it can never reach the catalogue fetch
        // as a relative Uri, with "[add new provider]" as the escape hatch.
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("api.example.com/v1", UriKind.RelativeOrAbsolute),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                }
            ],
            DefaultProvider = "default",
        };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("default");
            console.EnqueueChoice("[add new provider]");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://new.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://new.example.com/v1");
            await Assert.That(console.Prompts.Count(p => p.Title == "Select a provider:")).IsEqualTo(2);
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Invalid endpoint 'api.example.com/v1'");
            await Assert.That(console.ErrorMessages[0]).Contains("saved provider 'default'");
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
            console.EnqueueText("my-api-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.ApiKey).IsEqualTo("my-api-key");
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
            await Assert.That(result.ApiKey).IsEqualTo("saved-key");
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
            // The store throws AbsentKey at its source, so the surfaced
            // message is the store's own.
            await Assert.That(console.ErrorMessages[0]).Contains("No stored API key found");
            await Assert.That(console.ErrorMessages[0]).Contains("hgkey:default");
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
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? _ = await pipeline.RunAsync();
            
            LauncherSettings savedSettings = await store.LoadAsync();
            await Assert.That(savedSettings.Providers).IsEmpty();
            await Assert.That(savedSettings.DefaultProvider).IsEmpty();
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
            await cts.CancelAsync();

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
            await File.WriteAllTextAsync(
                store.SettingsFilePath,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 4 }""");

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
            await Assert.That(caught.Message).Contains("unsupported settings schema version");
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
            await Assert.That(result.Model).IsEqualTo("saved-model");
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
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings() with { DefaultProvider = "" };
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
            await Assert.That(console.ErrorMessages[0]).Contains("config set defaultProvider");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_dangling_default_pointer_writes_error_and_returns_null()
    {
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings() with { DefaultProvider = "ghost" };
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
            await Assert.That(console.ErrorMessages[0]).Contains("config set defaultProvider");
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
                isAvailable: true,
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            // Unavailable AND incompatible: its annotation must carry both
            // parts once the incompatible re-prompt turns annotations on.
            IHarnessAdapter unavailableOpenAi = WizardTestHelper.CreateTestAdapter(
                "UnavailableOpenAi",
                isAvailable: false,
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter("AnthropicCapable");
            WizardPipeline pipeline = new WizardPipeline(
                console, [openAiOnly, unavailableOpenAi, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("anthropic-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Endpoint).IsEqualTo("https://api.anthropic.example.com/v1");
            await Assert.That(result.ApiKey).IsEqualTo("anthropic-key");

            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
            await Assert.That(
                    console.Prompts[0].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsNull();
            await Assert.That(
                    console.Prompts[0].Choices.Single(c => c.Name == "UnavailableOpenAi").Annotation)
                .IsEqualTo("not installed");
            await Assert.That(
                    console.Prompts[1].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("incompatible: anthropic");
            await Assert.That(
                    console.Prompts[1].Choices.Single(c => c.Name == "UnavailableOpenAi").Annotation)
                .IsEqualTo("not installed; incompatible: anthropic");
            await Assert.That(
                    console.Prompts[1].Choices.Single(c => c.Name == "AnthropicCapable").Annotation)
                .IsNull();

            // Structured state the presenter consumes: the whole-line red
            // flag follows probe availability from the first prompt, and the
            // orange-segment state appears once the incompatible re-prompt
            // turns annotations on.
            await Assert.That(
                console.Prompts[0].Choices.Single(c => c.Name == "OpenAiOnly").Unavailable).IsFalse();
            await Assert.That(
                console.Prompts[0].Choices.Single(c => c.Name == "OpenAiOnly").IncompatibleProtocols)
                .IsNull();
            await Assert.That(
                console.Prompts[0].Choices.Single(c => c.Name == "UnavailableOpenAi").Unavailable)
                .IsTrue();
            await Assert.That(
                console.Prompts[0].Choices.Single(c => c.Name == "UnavailableOpenAi").IncompatibleProtocols)
                .IsNull();
            await Assert.That(
                console.Prompts[1].Choices.Single(c => c.Name == "OpenAiOnly").IncompatibleProtocols)
                .IsEqualTo("anthropic");
            await Assert.That(
                console.Prompts[1].Choices.Single(c => c.Name == "UnavailableOpenAi").IncompatibleProtocols)
                .IsEqualTo("anthropic");
            await Assert.That(
                console.Prompts[1].Choices.Single(c => c.Name == "AnthropicCapable").IncompatibleProtocols)
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
            console.EnqueueChoice("[add new provider]");
            console.EnqueueChoice("anthropic");
            console.EnqueueText("https://new.example.com/v1");
            console.EnqueueChoice("AnthropicCapable");
            // An "[add new provider]" session is keyless until the user types a
            // key: it never inherits the default entry's stored credential, so
            // the key prompt fires even though the default entry has one stored.
            console.EnqueueText("typed-key");
            console.EnqueueChoice("[skip]");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Endpoint).IsEqualTo("https://new.example.com/v1");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.ProviderName).IsNull();
            await Assert.That(result.ApiKey).IsEqualTo("typed-key");

            await Assert.That(console.Prompts.Count).IsEqualTo(7);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Select a provider:");
            await Assert.That(console.Prompts[2].Title).IsEqualTo("Protocol:");
            await Assert.That(
                console.Prompts[3].Title.StartsWith("Endpoint URL", StringComparison.Ordinal)).IsTrue();
            await Assert.That(console.Prompts[4].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[5].Title).IsEqualTo("API key (will not be saved):");
            await Assert.That(console.Prompts[6].Title).IsEqualTo("Model catalogue unavailable:");
            await Assert.That(
                    console.Prompts[4].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("incompatible: anthropic");
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
            console.EnqueueChoice("anthropic");
            console.EnqueueText("https://first-run.example.com/v1");
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("first-run-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.ApiKey).IsEqualTo("first-run-key");

            // No default backend: nothing to check at the harness step, but
            // the post-endpoint check still fires and loops back.
            await Assert.That(console.Prompts.Count).IsEqualTo(6);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Protocol:");
            await Assert.That(
                console.Prompts[2].Title.StartsWith("Endpoint URL", StringComparison.Ordinal)).IsTrue();
            await Assert.That(console.Prompts[3].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[4].Title).IsEqualTo("API key (will not be saved):");
            await Assert.That(console.Prompts[5].Title).IsEqualTo("Model catalogue unavailable:");
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("anthropic");
            await Assert.That(
                    console.Prompts[3].Choices.Single(c => c.Name == "OpenAiOnly").Annotation)
                .IsEqualTo("incompatible: anthropic");
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
            console.EnqueueText("ordered-key");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(console.Prompts.Count).IsEqualTo(4);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[1].Title).IsEqualTo("Select a provider:");
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

            LauncherSettings before = await store.LoadAsync();
            WizardResult? result = await pipeline.RunAsync();
            LauncherSettings after = await store.LoadAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
            await Assert.That(after.Providers.Count).IsEqualTo(before.Providers.Count);
            await Assert.That(after.Providers[0].Name).IsEqualTo(before.Providers[0].Name);
            await Assert.That(after.DefaultProvider).IsEqualTo(before.DefaultProvider);
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
            console.EnqueueText("required-model");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Model).IsEqualTo("required-model");
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
            console.EnqueueChoice("default");
            console.EnqueueChoice("[skip]");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Model).IsNull();
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Model catalogue unavailable:")).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_openai_endpoint_prompt_and_error_carry_the_include_v1_hint()
    {
        // The fresh-entry flow asks the protocol first so the endpoint prompt
        // and its refusal can name the OpenAI convention: /v1 belongs in the
        // base URL the harness appends paths to.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText(string.Empty);
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            const string hint = "OpenAI-style endpoints include /v1, e.g. https://api.example.com/v1";

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com/v1");
            await Assert.That(console.Prompts.Any(
                p => p.Title == $"Endpoint URL ({hint}):")).IsTrue();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains($"Endpoint URL is required ({hint})");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_anthropic_endpoint_prompt_and_error_carry_the_omit_v1_hint()
    {
        // Anthropic is the mirror image: Claude Code appends /v1/messages to
        // the base itself, so the prompt must steer the user away from a
        // /v1-suffixed base URL.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("anthropic");
            console.EnqueueText("api.example.com/v1");
            console.EnqueueText("https://api.example.com");

            WizardResult? result = await pipeline.RunAsync();

            const string hint = "Anthropic-style base URLs omit /v1 (the harness appends it), e.g. https://api.example.com";

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.Endpoint).IsEqualTo("https://api.example.com");
            await Assert.That(console.Prompts.Any(
                p => p.Title == $"Endpoint URL ({hint}):")).IsTrue();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("Invalid endpoint 'api.example.com/v1'");
            await Assert.That(console.ErrorMessages[0]).Contains($"({hint})");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_saved_provider_endpoint_keeps_a_query_ending_in_slash()
    {
        // The saved-provider return trims only the path's trailing slash: a
        // whole-string TrimEnd('/') used to chop the "/" off a query
        // (https://host/v1?next=/ became https://host/v1?next=).
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://host/v1?next=/",
            protocol: ProtocolKind.OpenAiChat);
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter();
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("TestAgent");
            console.EnqueueChoice("default");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://host/v1?next=/");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
