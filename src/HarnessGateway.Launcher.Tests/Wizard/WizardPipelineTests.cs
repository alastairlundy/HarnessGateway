using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;
using HarnessGateway.Launcher.Wizard;
using HarnessGateway.Launcher.Wizard.Models;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class WizardPipelineTests
{
    /// <summary>
    /// A provider whose default is openai-chat and a single stored model
    /// entry overriding both protocol and endpoint to Anthropic - the shape a
    /// per-model gateway creates, and the one where an adopted endpoint with a
    /// non-adopted protocol would be visible.
    /// </summary>
    private static LauncherSettings CreateStoredOverrideSettings() => new()
    {
        Providers =
        [
            new Provider
            {
                Name = "default",
                BaseUri = new Uri("https://provider.example/v1"),
                DefaultProtocol = ProtocolKind.OpenAiChat,
                ApiKey = "hgkey:default",
                Models =
                [
                    new ModelEntry
                    {
                        Name = "claude-x",
                        Protocol = ProtocolKind.Anthropic,
                        Endpoint = new Uri("https://anthropic-gw.example/v1"),
                    }
                ],
            }
        ],
        DefaultProvider = "default",
    };

    [Test]
    public async Task RunAsync_harness_step_shows_unavailable_annotated()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter available = WizardTestHelper.CreateTestAdapter("AvailableAgent", availability: AvailabilityResult.Found);
            IHarnessAdapter unavailable = WizardTestHelper.CreateTestAdapter("UnavailableAgent", availability: AvailabilityResult.Absent);
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
            IHarnessAdapter unavailable = WizardTestHelper.CreateTestAdapter("UnavailableAgent", availability: AvailabilityResult.Absent);
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
            await Assert.That(console.ErrorMessages[0]).Contains("<install hint for UnavailableAgent>");
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
            IHarnessAdapter first = WizardTestHelper.CreateTestAdapter("AgentA", availability: AvailabilityResult.Absent);
            IHarnessAdapter second = WizardTestHelper.CreateTestAdapter("AgentB", availability: AvailabilityResult.Absent);
            WizardPipeline pipeline = new WizardPipeline(console, [first, second], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0]).Contains("No agent harness is installed");
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("<install hint for AgentA>"))).IsTrue();
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("<install hint for AgentB>"))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_no_harness_confirmed_says_the_checks_failed_rather_than_nothing_installed()
    {
        // Every probe failed, so nothing was found and nothing was ruled
        // out: "no agent harness is installed" would claim more than the
        // run established.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter first = WizardTestHelper.CreateTestAdapter(
                "AgentA", availability: AvailabilityResult.Unknown("probe blew up"));
            IHarnessAdapter second = WizardTestHelper.CreateTestAdapter(
                "AgentB", availability: AvailabilityResult.Unknown("probe blew up"));
            WizardPipeline pipeline = new WizardPipeline(console, [first, second], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages[0])
                .Contains("No agent harness could be confirmed installed");
            await Assert.That(console.ErrorMessages[0])
                .DoesNotContain("No agent harness is installed");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_picking_a_harness_whose_check_failed_reports_the_failure_not_an_install()
    {
        // Third availability state at the picker: the probe could not run,
        // so the wizard must not tell the user to install anything - it
        // never established that anything is missing.
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter checkFailed = WizardTestHelper.CreateTestAdapter(
                "UncheckedAgent",
                availability: AvailabilityResult.Unknown("Win32Exception: Access is denied."));
            IHarnessAdapter available = WizardTestHelper.CreateTestAdapter("AvailableAgent");
            WizardPipeline pipeline = new WizardPipeline(console, [checkFailed, available], store, keyStore);

            console.EnqueueChoice("UncheckedAgent");
            console.EnqueueChoice("AvailableAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AvailableAgent");
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0])
                .Contains("Could not check whether UncheckedAgent is installed");
            await Assert.That(console.ErrorMessages[0]).Contains("Win32Exception: Access is denied.");
            await Assert.That(console.ErrorMessages[0]).DoesNotContain("<install hint for UncheckedAgent>");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_annotates_a_failed_check_as_check_failed_rather_than_not_installed()
    {
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment();
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter checkFailed = WizardTestHelper.CreateTestAdapter(
                "UncheckedAgent",
                availability: AvailabilityResult.Unknown("probe blew up"));
            IHarnessAdapter available = WizardTestHelper.CreateTestAdapter("AvailableAgent");
            WizardPipeline pipeline = new WizardPipeline(console, [checkFailed, available], store, keyStore);

            console.EnqueueChoice("AvailableAgent");
            console.EnqueueChoice("openai-chat");
            console.EnqueueText("https://api.example.com/v1");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AvailableAgent");

            // The list labels a harness the probe never got an answer about
            // as unverified, not as missing - and still refuses to offer it.
            await Assert.That(
                    console.Prompts[0].Choices.Single(c => c.Name == "UncheckedAgent").Annotation)
                .IsEqualTo("check failed");
            await Assert.That(
                    console.Prompts[0].Choices.Single(c => c.Name == "UncheckedAgent").Unavailable)
                .IsTrue();
            await Assert.That(
                    console.Prompts[0].Choices.Single(c => c.Name == "UncheckedAgent").Annotation)
                .DoesNotContain("not installed");
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
                availability: AvailabilityResult.Absent,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.Anthropic });
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages).IsNotEmpty();
            await Assert.That(console.ErrorMessages[0])
                .Contains("No installed agent harness supports the anthropic protocol");
            await Assert.That(console.ErrorMessages.Any(m => m.Contains("<install hint for AnthropicCapable>")))
                .IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_protocol_abort_reports_a_failed_check_as_unconfirmed_rather_than_absent()
    {
        // Same abort, different claim: the only anthropic-capable harness
        // has a probe that never ran, so "no installed harness supports
        // it" would be a finding this run never made.
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
                availability: AvailabilityResult.Unknown("probe blew up"),
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.Anthropic });
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly, anthropicCapable], store, keyStore);

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNull();
            await Assert.That(console.Prompts).IsEmpty();
            await Assert.That(console.ErrorMessages[0])
                .Contains("No agent harness could be confirmed as supporting the anthropic protocol");
            await Assert.That(console.ErrorMessages[0])
                .DoesNotContain("No installed agent harness supports");
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
                console.Prompts.Any(p => p.Title == "Model catalog unavailable:")).IsFalse();
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
                availability: AvailabilityResult.Found,
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            // Unavailable AND incompatible: its annotation must carry both
            // parts once the incompatible re-prompt turns annotations on.
            IHarnessAdapter unavailableOpenAi = WizardTestHelper.CreateTestAdapter(
                "UnavailableOpenAi",
                availability: AvailabilityResult.Absent,
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
            await Assert.That(console.Prompts[6].Title).IsEqualTo("Model catalog unavailable:");
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
            await Assert.That(console.Prompts[5].Title).IsEqualTo("Model catalog unavailable:");
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
            await Assert.That(console.Prompts[3].Title).IsEqualTo("Model catalog unavailable:");
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
                console.Prompts.Any(p => p.Title == "Model catalog unavailable:")).IsFalse();
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
                console.Prompts.Count(p => p.Title == "Model catalog unavailable:")).IsEqualTo(1);
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

    [Test]
    public async Task RunAsync_post_model_protocol_mismatch_loops_back_to_harness()
    {
        // The model step can return a protocol the chosen harness cannot
        // speak (here: openai-responses under an openai-chat provider). The
        // wizard must surface the incompatible message and return to the
        // harness step instead of completing and letting the launch flow
        // refuse with exit 3 afterwards.
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.example.com/v1",
            protocol: ProtocolKind.OpenAiChat,
            apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            StubHttpMessageHandler handler = new("""{"data":[{"id":"gpt-4"}]}""");
            HttpClient http = new HttpClient(handler);
            ModelCatalogClient catalogClient = new ModelCatalogClient(http);

            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            IHarnessAdapter responsesCapable = WizardTestHelper.CreateTestAdapter(
                "ResponsesCapable",
                supportedProtocols: new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses
                });
            WizardPipeline pipeline = new WizardPipeline(
                console, [openAiOnly, responsesCapable], store, keyStore, catalogClient);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("default");
            console.EnqueueChoice("gpt-4");
            console.EnqueueChoice("openai-responses");
            // Loop-back: the harness list is re-presented with annotations,
            // and a harness that can speak the effective protocol is picked.
            console.EnqueueChoice("ResponsesCapable");
            // The model selection was discarded on loop-back and is collected
            // again from scratch.
            console.EnqueueChoice("gpt-4");
            console.EnqueueChoice("openai-responses");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("ResponsesCapable");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
            await Assert.That(result.Model).IsEqualTo("gpt-4");
            await Assert.That(result.ApiKey).IsEqualTo("saved-key");

            // Two harness prompts and two model prompts: the mismatch looped
            // back rather than completing, and the model was re-collected.
            await Assert.That(console.Prompts.Count).IsEqualTo(7);
            await Assert.That(console.Prompts[0].Title).IsEqualTo("Select a harness:");
            await Assert.That(console.Prompts[4].Title).IsEqualTo("Select a harness:");
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a model (or skip):")).IsEqualTo(2);

            // The incompatible message names the harness and the model's
            // effective protocol, and nothing else failed along the way.
            await Assert.That(console.ErrorMessages.Count).IsEqualTo(1);
            await Assert.That(console.ErrorMessages[0]).Contains("Refusing to launch OpenAiOnly");
            await Assert.That(console.ErrorMessages[0]).Contains("openai-responses");

            // The re-presented harness list keeps the annotations visible.
            await Assert.That(
                    console.Prompts[4].Choices.Single(c => c.Name == "OpenAiOnly").IncompatibleProtocols)
                .IsEqualTo("openai-responses");
            await Assert.That(
                    console.Prompts[4].Choices.Single(c => c.Name == "ResponsesCapable").IncompatibleProtocols)
                .IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_post_model_dead_end_aborts_instead_of_looping()
    {
        // No installed harness can speak openai-responses: after the
        // mismatch message the loop-top dead-end guard must abort with
        // NoHarnessSupportsProtocol rather than re-prompting forever.
        LauncherSettings settings = WizardTestHelper.CreateDefaultSettings(
            endpoint: "https://api.example.com/v1",
            protocol: ProtocolKind.OpenAiChat,
            apiKey: "hgkey:default");
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) = WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            StubHttpMessageHandler handler = new("""{"data":[{"id":"gpt-4"}]}""");
            HttpClient http = new HttpClient(handler);
            ModelCatalogClient catalogClient = new ModelCatalogClient(http);

            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            WizardPipeline pipeline = new WizardPipeline(
                console, [openAiOnly], store, keyStore, catalogClient);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("default");
            console.EnqueueChoice("gpt-4");
            console.EnqueueChoice("openai-responses");

            WizardOutcome outcome = await pipeline.RunAsync();

            WizardAborted? aborted = outcome as WizardAborted;
            await Assert.That(aborted).IsNotNull();
            await Assert.That(aborted!.Reason).IsEqualTo(WizardAbortReason.NoHarnessSupportsProtocol);

            // The incompatible message comes first, then the dead-end
            // refusal; the guard fires before a second harness prompt, so
            // the wizard cannot loop.
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(1);
            await Assert.That(console.ErrorMessages.Count).IsEqualTo(2);
            await Assert.That(console.ErrorMessages[0]).Contains("Refusing to launch OpenAiOnly");
            await Assert.That(console.ErrorMessages[1]).Contains("No agent harness supports");
            await Assert.That(console.ErrorMessages[1]).Contains("openai-responses");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_model_endpoint_override_swaps_the_session_endpoint_and_names_it()
    {
        // A stored endpoint on the picked model's entry goes into the
        // session endpoint the wizard returns - the launch request's flag
        // pair - and a line names it, so the swap is visible. The wizard
        // stays write-free: the override is only ever read.
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://provider.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                    Models =
                    [
                        new ModelEntry
                        {
                            Name = "gpt-4o",
                            Endpoint = new Uri("https://per-model.example/v1"),
                        }
                    ],
                }
            ],
            DefaultProvider = "default",
        };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter(
                "RequiredAgent", requiresModel: true);
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("RequiredAgent");
            console.EnqueueChoice("default");
            console.EnqueueText("gpt-4o");

            string before = Convert.ToBase64String(File.ReadAllBytes(store.SettingsFilePath));
            WizardResult? result = await pipeline.RunAsync();
            string after = Convert.ToBase64String(File.ReadAllBytes(store.SettingsFilePath));

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Endpoint).IsEqualTo("https://per-model.example/v1");
            await Assert.That(result.Model).IsEqualTo("gpt-4o");

            // The naming line's exact wording is non-contractual UX: only
            // that it names the endpoint actually used is asserted.
            await Assert.That(console.WrittenMessages.Any(
                message => message.Contains("https://per-model.example/v1"))).IsTrue();

            // No settings write anywhere.
            await Assert.That(after).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_escapes_bracketed_override_values_in_the_naming_line()
    {
        // The naming line is handed to WriteMarkup, which parses Spectre
        // markup: a stored model name or endpoint containing "[...]" (here a
        // bracketed IPv6 literal and a bracketed model tag) must be escaped
        // there or the wizard throws instead of completing. The values the
        // launch request receives stay raw - escaping is display-only.
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://provider.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                    Models =
                    [
                        new ModelEntry
                        {
                            Name = "gpt-4o [beta]",
                            Endpoint = new Uri("http://[::1]:8080/v1"),
                        }
                    ],
                }
            ],
            DefaultProvider = "default",
        };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter(
                "RequiredAgent", requiresModel: true);
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("RequiredAgent");
            console.EnqueueChoice("default");
            console.EnqueueText("gpt-4o [beta]");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Model).IsEqualTo("gpt-4o [beta]");
            await Assert.That(result.Endpoint).IsEqualTo("http://[::1]:8080/v1");

            await Assert.That(console.WrittenMessages.Any(
                message => message.Contains("Using endpoint")
                           && message.Contains("[[beta]]")
                           && message.Contains("[[::1]]"))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_stored_model_protocol_and_endpoint_return_one_consistent_pair()
    {
        // The model step carries no protocol of its own on the degraded path,
        // so the stored entry's override supplies it - the same entry the
        // endpoint swap reads, which keeps the returned pair agreed.
        LauncherSettings settings = CreateStoredOverrideSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter adapter = WizardTestHelper.CreateTestAdapter(
                "AnthropicCapable", requiresModel: true);
            WizardPipeline pipeline = new WizardPipeline(console, [adapter], store, keyStore);

            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueChoice("default");
            console.EnqueueText("claude-x");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.Endpoint).IsEqualTo("https://anthropic-gw.example/v1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_stored_model_protocol_drives_the_compatibility_check()
    {
        // Same stored entry, but a harness that only speaks openai-chat: the
        // post-model check must run against the stored protocol rather than
        // the provider default, so the wizard refuses instead of completing
        // with an anthropic endpoint on an openai-chat session.
        LauncherSettings settings = CreateStoredOverrideSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                requiresModel: true,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            WizardPipeline pipeline = new WizardPipeline(console, [openAiOnly], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("default");
            console.EnqueueText("claude-x");

            WizardOutcome outcome = await pipeline.RunAsync();

            WizardAborted? aborted = outcome as WizardAborted;
            await Assert.That(aborted).IsNotNull();
            await Assert.That(aborted!.Reason).IsEqualTo(WizardAbortReason.NoHarnessSupportsProtocol);
            await Assert.That(console.ErrorMessages.Any(
                message => message.Contains("anthropic"))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_stored_model_protocol_unblocks_the_provider_default_gates()
    {
        // The default provider's protocol is openai-chat, but its only stored
        // model overrides to anthropic and the only installed harness speaks
        // anthropic. The provider default used to seed the harness step (its
        // dead-end guard aborted: nothing speaks openai-chat) and the
        // establishing gate re-checked the harness against it (dead-end
        // abort again) - both blocked a launch the stored model fully
        // supports. The post-model check still gates the protocol that
        // actually ships.
        LauncherSettings settings = CreateStoredOverrideSettings();
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter anthropicOnly = WizardTestHelper.CreateTestAdapter(
                "AnthropicCapable",
                requiresModel: true,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.Anthropic });
            WizardPipeline pipeline = new WizardPipeline(console, [anthropicOnly], store, keyStore);

            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueChoice("default");
            console.EnqueueText("claude-x");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.Endpoint).IsEqualTo("https://anthropic-gw.example/v1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_model_protocol_retry_skips_the_provider_protocol_gate()
    {
        // Pass 1 picks an openai-only harness, the model resolves to the
        // stored anthropic override, and the post-model check loops back. The
        // retry can only select an anthropic harness - which the provider
        // protocol gate used to reject, forcing the pick back to the openai
        // harness and bouncing forever between the two gates (no dead-end
        // guard fires: some harness speaks each protocol). The gate now
        // applies only to the establishing pass, so the retry reaches the
        // model step and the post-model check decides.
        //
        // The script is shaped so the pre-fix path terminates with the wrong
        // pair rather than hanging the run: the openai harness does not
        // require a model, so its model step is the degraded choice prompt
        // ([manual]/[skip]) while the anthropic harness's is a text prompt.
        // The paths therefore drain different queues - fixed: the two model
        // texts and two harness picks; pre-fix: one model text, the [skip]
        // choice, and three harness picks - and each finishes on its own last
        // input, leaving the wrong result for the assertions to catch.
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://provider.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                    Models =
                    [
                        new ModelEntry
                        {
                            Name = "claude-x",
                            Protocol = ProtocolKind.Anthropic,
                            Endpoint = new Uri("https://anthropic-gw.example/v1"),
                        },
                        new ModelEntry
                        {
                            Name = "claude-y",
                            Protocol = ProtocolKind.Anthropic,
                            Endpoint = new Uri("https://anthropic-gw.example/v1"),
                        },
                    ],
                }
            ],
            DefaultProvider = "default",
        };
        (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) =
            WizardTestHelper.CreateTestEnvironment(settings);
        keyStore.Seed("hgkey:default", "saved-key");
        try
        {
            TestConsole console = new TestConsole();
            IHarnessAdapter openAiOnly = WizardTestHelper.CreateTestAdapter(
                "OpenAiOnly",
                requiresModel: false,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
            IHarnessAdapter anthropicCapable = WizardTestHelper.CreateTestAdapter(
                "AnthropicCapable",
                requiresModel: true,
                supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.Anthropic });
            WizardPipeline pipeline = new WizardPipeline(
                console, [openAiOnly, anthropicCapable], store, keyStore);

            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("default");
            // Pass 1: the openai harness's model step is a choice prompt, so
            // [manual] opens the text prompt that names the stored override.
            console.EnqueueChoice("[manual]");
            console.EnqueueText("claude-x");
            // Retry, forced onto the anthropic harness by the loop-back.
            console.EnqueueChoice("AnthropicCapable");
            console.EnqueueText("claude-y");
            // Pre-fix path only: the gate bounced the retry back to openai,
            // where [skip] ends the session without a stored override.
            console.EnqueueChoice("OpenAiOnly");
            console.EnqueueChoice("[skip]");

            WizardResult? result = await pipeline.RunAsync();

            await Assert.That(result).IsNotNull();
            await Assert.That(result.AgentCommandName).IsEqualTo("AnthropicCapable");
            await Assert.That(result.Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(result.Endpoint).IsEqualTo("https://anthropic-gw.example/v1");
            await Assert.That(result.Model).IsEqualTo("claude-y");

            // The retry went straight to the model step: two harness picks,
            // not three.
            await Assert.That(
                console.Prompts.Count(p => p.Title == "Select a harness:")).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
