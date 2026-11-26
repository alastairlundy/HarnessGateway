using System.Reflection;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Launch;

/// <summary>
/// Pins the flow interface itself: the structural restore guarantee,
/// refusal and key folding, the pair rule, and persist ordering. The
/// orchestrator keeps mapping/wiring tests - no end-to-end duplication here.
/// Did-you-mean hint wording is deliberately never pinned: it is
/// non-contractual UX.
/// </summary>
public class LaunchFlowTests
{
    private static readonly HashSet<ProtocolKind> AllProtocols =
    [
        ProtocolKind.OpenAiChat,
        ProtocolKind.OpenAiResponses,
        ProtocolKind.Anthropic,
    ];

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (LaunchFlow flow, string tempDir, string settingsDir, string backupsDir) CreateFlow(
        IHarnessAdapter? adapter = null,
        LauncherSettings? settings = null,
        ILaunchRunner? runner = null,
        ISecureKeyStore? keyStore = null,
        IProtocolEmissionPlanner? emissionPlanner = null,
        ITerminalProbe? terminalProbe = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        ISecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        SettingsStore settingsStore = new(settingsDir, effectiveKeyStore);
        if (settings is not null)
        {
            settingsStore.Save(settings);
        }

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            adapter is not null ? [adapter] : [],
            effectiveKeyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            emissionPlanner ?? new ProtocolEmissionPlanner(),
            terminalProbe ?? new FakeTerminalProbe());
        return (flow, tempDir, settingsDir, backupsDir);
    }

    private static (LaunchFlow flow, string tempDir, string configPath, string settingsDir) CreateConfigFlow(
        string originalConfig,
        Func<string, ILaunchRunner>? runnerFactory = null)
    {
        string tempDir = CreateTempDir();
        string configFile = Path.Combine(tempDir, "harness-config.toml");
        File.WriteAllText(configFile, originalConfig);

        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        SettingsStore settingsStore = new(settingsDir, new FakeSecureKeyStore());
        settingsStore.Save(DefaultSettings("test-backend"));

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            runnerFactory?.Invoke(configFile) ?? new TestLaunchRunner(),
            [new ConfigWritingAdapter(configFile)],
            new FakeSecureKeyStore(),
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            new FakeTerminalProbe());
        return (flow, tempDir, configFile, settingsDir);
    }

    private static (LaunchFlow flow, string tempDir, string configPath, string backupsDir) CreateCodexFlow(
        string userOwnedConfig,
        ILaunchRunner? runner = null,
        string? apiKey = null,
        bool codexAvailable = true)
    {
        string tempDir = CreateTempDir();
        string codexHome = Path.Combine(tempDir, "codex-home");
        Directory.CreateDirectory(codexHome);
        string configPath = Path.Combine(codexHome, "config.toml");
        File.WriteAllText(configPath, userOwnedConfig);

        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore keyStore = new();
        string? keyReference = SeedKey(keyStore, "codex-backend", apiKey);

        SettingsStore settingsStore = new(settingsDir, keyStore);
        settingsStore.Save(new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "codex-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiResponses,
                    ApiKey = keyReference
                },
            ],
            DefaultProvider = "codex-backend",
        });

        CodexAdapter adapter = new(
            (_, _) => Task.FromResult(codexAvailable),
            name => name == "CODEX_HOME" ? codexHome : null);

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            [adapter],
            keyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            new FakeTerminalProbe());
        return (flow, tempDir, configPath, backupsDir);
    }

    private static (LaunchFlow flow, string tempDir, string backupsDir) CreateClaudeFlow(
        bool available = true,
        string? apiKey = null,
        FakeSecureKeyStore? keyStore = null,
        ILaunchRunner? runner = null,
        ITerminalProbe? terminalProbe = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        string? keyReference = SeedKey(effectiveKeyStore, "claude-backend", apiKey);

        SettingsStore settingsStore = new(settingsDir, effectiveKeyStore);
        settingsStore.Save(new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "claude-backend",
                    BaseUri = new Uri("https://api.example.com"),
                    DefaultProtocol = ProtocolKind.Anthropic,
                    ApiKey = keyReference
                },
            ],
            DefaultProvider = "claude-backend",
        });

        // Fake detection delegate: availability never touches the real PATH.
        ClaudeCodeAdapter adapter = new((_, _) => Task.FromResult(available));

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            [adapter],
            effectiveKeyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            terminalProbe ?? new FakeTerminalProbe());
        return (flow, tempDir, backupsDir);
    }

    /// <summary>
    /// Settings fixtures only ever carry the reference; the secret itself is
    /// seeded into the fake store the way the real store would hold it.
    /// </summary>
    private static string? SeedKey(FakeSecureKeyStore keyStore, string providerName, string? secret)
    {
        if (secret is null)
        {
            return null;
        }

        string reference = KeyReference.ForProvider(providerName);
        keyStore.Seed(reference, secret);
        return reference;
    }

    private static LauncherSettings DefaultSettings(string name = "default") => new()
    {
        Providers =
        [
            new Provider
            {
                Name = name,
                BaseUri = new Uri("https://api.default.com/v1"),
                DefaultProtocol = ProtocolKind.OpenAiChat,
            },
        ],
        DefaultProvider = name,
    };

    /// <summary>
    /// The blocking-run payload these CLI-shaped tests assert on, read
    /// through a cast that fails loudly if a completed outcome ever carries
    /// the detached <see cref="CompletedPayload.HandedOff"/> variant instead.
    /// </summary>
    private static int ExitStatusOf(LaunchCompleted completed) =>
        ((CompletedPayload.ExitStatus)completed.Payload).Value;

    // --- Restore guarantee: structural, on every exit path ---

    [Test]
    public async Task ExecuteAsync_runner_fault_restores_config_and_folds_to_unexpected()
    {
        const string originalConfig = "# original config\n";
        (LaunchFlow flow, string tempDir, string configPath, _) = CreateConfigFlow(
            originalConfig,
            runnerFactory: _ => new FaultingLaunchRunner(new InvalidOperationException("runner exploded")));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("ConfigWriter", null, null, null, null, [], null));

            // The primary pin: the fault does not leak as an exception, and
            // the finally still restored the launcher-written config.
            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.Unexpected);
            await Assert.That(failed.Message).IsEqualTo("runner exploded");
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(originalConfig);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_config_write_fault_folds_to_unexpected_without_running()
    {
        string tempDir = CreateTempDir();
        try
        {
            string settingsDir = Path.Combine(tempDir, "settings");
            Directory.CreateDirectory(settingsDir);
            string backupsDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupsDir);

            // A directory squatting on the write target: the write itself
            // faults on every platform, before the runner could ever start.
            string blockedTarget = Path.Combine(tempDir, "blocked-config.toml");
            Directory.CreateDirectory(blockedTarget);

            SettingsStore settingsStore = new(settingsDir, new FakeSecureKeyStore());
            settingsStore.Save(DefaultSettings("test-backend"));

            CountingLaunchRunner runner = new();
            LaunchFlow flow = new(
                settingsStore,
                new BackupService(backupsDir),
                runner,
                [new ConfigWritingAdapter(blockedTarget)],
                new FakeSecureKeyStore(),
                new FakeGuiInstallDiscovery(),
                new FakeRunningInstanceDetector(),
                new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            new FakeTerminalProbe());

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("ConfigWriter", null, null, null, null, [], null));

            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.Unexpected);
            // The fault happened before the run, so the runner never started.
            await Assert.That(runner.RunCallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_no_registered_writes_restores_as_noop_and_completes()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, string backupsDir) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            // Nothing was ever registered, so the restore was a clean no-op.
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_config_keeps_the_write()
    {
        const string originalConfig = "# original config\n";
        (LaunchFlow flow, string tempDir, string configPath, _) = CreateConfigFlow(originalConfig);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("ConfigWriter", null, null, null, null, ["config"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo("{\"modified\":true}");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_config_opts_out_even_when_the_runner_faults()
    {
        const string originalConfig = "# original config\n";
        (LaunchFlow flow, string tempDir, string configPath, _) = CreateConfigFlow(
            originalConfig,
            runnerFactory: _ => new FaultingLaunchRunner(new InvalidOperationException("runner exploded")));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("ConfigWriter", null, null, null, null, ["config"], null));

            // The opt-out is honoured on the fault path too: the write is
            // durable by design, so the finally discards the marker instead
            // of restoring.
            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.Unexpected);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo("{\"modified\":true}");
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"), "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Refusal folding ---

    [Test]
    public async Task ExecuteAsync_codex_refusal_folds_to_provider_block_conflict()
    {
        const string userOwnedConfig =
            "# my provider block\n"
            + "[model_providers.harnessgateway]\n"
            + "name = \"Mine\"\n"
            + "base_url = \"https://mine.example/v1\"\n"
            + "env_key = \"MINE_KEY\"\n"
            + "wire_api = \"responses\"\n";
        (LaunchFlow flow, string tempDir, string configPath, _) = CreateCodexFlow(userOwnedConfig);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("codex", null, null, null, "gpt-5", [], null));

            // The adapter's planning-time refusal never escapes as an
            // exception: it folds into the closed outcome family.
            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.ProviderBlockConflict);
            await Assert.That(refused.Message).Contains("already defines a");
            // Refused before any write, so the user-owned file is untouched.
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(userOwnedConfig);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Key folds ---

    [Test]
    public async Task ExecuteAsync_absent_key_folds_to_credential_store_absent_key()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                },
            ],
            DefaultProvider = "default",
        };

        // Reference present, secret absent from the store.
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings,
            keyStore: new FakeSecureKeyStore());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.CredentialStoreAbsentKey);
            await Assert.That(failed.Message).Contains("No stored API key found");
            await Assert.That(failed.Message).Contains("hgkey:default");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_key_store_fault_folds_to_credential_store_error()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                },
            ],
            DefaultProvider = "default",
        };

        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings,
            keyStore: new FaultingKeyStore());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.CredentialStoreError);
            await Assert.That(failed.Message).Contains("credential store");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_session_key_rides_the_launch_copy_and_never_the_stored_entry()
    {
        // A stored entry with no key: the wizard-collected SessionKey fills
        // the gap on the launch copy only.
        LauncherSettings settings = DefaultSettings("test-backend");
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(
            adapter: adapter,
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, "gpt-5", ["model"], "sk-session"));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            // The launch copy carries the secret...
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest.ApiKey).IsEqualTo("sk-session");

            // ...and the --persist model save never does: the settings file
            // keeps its reference/null key shape, never launch-time material.
            string settingsBytes = await File.ReadAllTextAsync(
                Path.Combine(settingsDir, "HarnessGateway-launcher.json"));
            await Assert.That(settingsBytes.Contains("sk-session")).IsFalse();

            LauncherSettings saved = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(saved.Providers[0].DefaultModel).IsEqualTo("gpt-5");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Pair rule and resolution refusals ---

    [Test]
    public async Task ExecuteAsync_endpoint_without_protocol_folds_to_refusal()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, "https://api.example.com/v1", null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.EndpointWithoutProtocol);
            await Assert.That(refused.Message).Contains("--endpoint requires --protocol");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_protocol_without_endpoint_folds_to_refusal()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, "openai-chat", null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.ProtocolWithoutEndpoint);
            await Assert.That(refused.Message).Contains("--protocol requires --endpoint");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_invalid_endpoint_folds_to_refusal()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, "not-a-url", "openai-chat", null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.InvalidEndpoint);
            await Assert.That(refused.Message).Contains("must be an absolute URL");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_invalid_protocol_folds_to_refusal()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, "https://api.example.com/v1", "smoke-signal", null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.InvalidProtocol);
            await Assert.That(refused.Message).Contains("Invalid protocol");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unknown_provider_name_folds_to_refusal()
    {
        LauncherSettings settings = DefaultSettings("primary");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", "nope", null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.UnknownProviderName);
            await Assert.That(refused.Message).Contains("unknown provider name 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_dangling_default_provider_folds_to_refusal()
    {
        LauncherSettings settings = DefaultSettings("primary") with { DefaultProvider = "ghost" };
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.DanglingDefaultProvider);
            await Assert.That(refused.Message).Contains("config set defaultProvider");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unlisted_model_folds_to_refusal()
    {
        // A non-empty models array is the closed set launch accepts; a
        // --model naming no entry is a user-rectifiable refusal (exit 3 via
        // the UnknownAgent row, mapped in LaunchOrchestratorTests).
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "primary",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    Models = [new ModelEntry { Name = "gpt-5" }],
                },
            ],
            DefaultProvider = "primary",
        };

        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, "gpt-4", [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.UnlistedModel);
            await Assert.That(refused.Message).Contains("'gpt-4'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_absent_default_provider_folds_to_refusal()
    {
        // DefaultProvider intentionally unset: multiple entries must not fall
        // back to the first one - the pointer is the only read.
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                },
                new Provider
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiResponses,
                },
            ],
        };

        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.AbsentDefaultProvider);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_empty_settings_folds_to_no_provider()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.NoProvider);
            // The usage-error wording is contractual; no "Launch failed:"
            // prefix may borrow the internal-fault marker.
            await Assert.That(refused.Message).Contains("No provider is configured");
            await Assert.That(refused.Message).DoesNotContain("Launch failed:");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unknown_harness_folds_with_the_lookup_message()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            new TestHarnessAdapter("TestAgent"),
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("GhostHarness", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.UnknownHarness);
            // The deterministic prefix is pinned; the did-you-mean suffix is
            // non-contractual and deliberately not asserted.
            await Assert.That(refused.Message).Contains("Unknown harness: GhostHarness.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unavailable_harness_folds_with_the_install_hint()
    {
        TestHarnessAdapter adapter = new(
            "TestAgent",
            isAvailable: false,
            installHint: "Install via: npm install -g test");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.HarnessUnavailable);
            await Assert.That(refused.Message).IsEqualTo("Install via: npm install -g test");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unsupported_protocol_folds_before_any_write()
    {
        // Fake harness that only speaks openai-chat, pointed at an anthropic
        // backend: refused before planning, so nothing is ever backed up.
        TestHarnessAdapter adapter = new(
            "TestAgent",
            supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "anthropic-backend",
                    BaseUri = new Uri("https://api.anthropic.com"),
                    DefaultProtocol = ProtocolKind.Anthropic,
                },
            ],
            DefaultProvider = "anthropic-backend",
        };

        (LaunchFlow flow, string tempDir, _, string backupsDir) = CreateFlow(
            adapter: adapter,
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.ProtocolNotSupported);
            await Assert.That(refused.Message).Contains("Refusing to launch TestAgent");
            await Assert.That(refused.Message).Contains("anthropic");
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Persist ordering ---

    [Test]
    public async Task ExecuteAsync_model_persist_runs_after_run_and_restore()
    {
        const string originalConfig = "# original config\n";
        (LaunchFlow flow, string tempDir, string configPath, string settingsDir) =
            CreateConfigFlow(originalConfig);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "ConfigWriter", null, null, null, "gpt-5", ["model"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            // Both post-conditions of one call: the restore put the original
            // config back, and the model landed in settings afterwards.
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(originalConfig);
            LauncherSettings saved = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(saved.Providers[0].DefaultModel).IsEqualTo("gpt-5");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_restore_fault_skips_model_persist_and_folds_to_unexpected()
    {
        const string originalConfig = "# original config\n";

        // The runner swaps the written config file for a directory, so the
        // finally's restore cannot write the original bytes back and faults
        // on every platform.
        (LaunchFlow flow, string tempDir, _, string settingsDir) = CreateConfigFlow(
            originalConfig,
            runnerFactory: configPath => new CallbackLaunchRunner(_ =>
            {
                File.Delete(configPath);
                Directory.CreateDirectory(configPath);
                return Task.FromResult(0);
            }));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "ConfigWriter", null, null, null, "gpt-5", ["model"], null));

            // The restore fault surfaces as the terminal fold, not a leak -
            // today's exit-7 parity - and it skips the model persist, which
            // is exactly what places restore before persist.
            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.Unexpected);
            LauncherSettings saved = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(saved.Providers[0].DefaultModel).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- End-to-end scenarios: assertions speak the outcome vocabulary,
    //     never exit codes. ---

    [Test]
    public async Task ExecuteAsync_endpoint_and_protocol_override_launches_the_ad_hoc_case()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(adapter: adapter);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "TestAgent", null, "https://api.example.com/v1", "openai-chat", null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            // No stored provider exists, so the payload carries the ad-hoc
            // case itself - never a synthesised stored-looking provider.
            await Assert.That(adapter.LastRequest.Launch)
                .IsTypeOf<LaunchResolution.AdHocCase>();
            await Assert.That(adapter.LastRequest.Launch.BaseUri)
                .IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(adapter.LastRequest.Launch.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_hands_the_injected_planner_and_family_to_the_adapter()
    {
        // The planner reaches the adapter through wiring - the
        // composition root registers it and the flow forwards it on the
        // payload - never through a static. The payload carries the
        // resolved family itself alongside it.
        FakeProtocolEmissionPlanner planner = new();
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings(),
            emissionPlanner: planner);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest.EmissionPlanner).IsSameReferenceAs(planner);
            await Assert.That(adapter.LastRequest.Launch)
                .IsTypeOf<LaunchResolution.ProviderCase>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_without_overrides_uses_the_default_provider()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            // The payload carries the family itself: the stored entry the
            // default pointer selected is carried by the provider case.
            LaunchResolution.ProviderCase providerCase =
                (LaunchResolution.ProviderCase)adapter.LastRequest.Launch;
            await Assert.That(providerCase.Provider.Name).IsEqualTo("default");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_model_required_but_missing_warns_and_still_launches()
    {
        TestHarnessAdapter adapter = new("TestAgent", requiresModel: true);
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            (LaunchOutcome outcome, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                flow.ExecuteAsync(
                    new LaunchRequest("TestAgent", null, null, null, null, [], null)));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(errorOutput).Contains("requires a model but none was specified");
            // The warning never blocks: planning still produced a request.
            await Assert.That(adapter.LastRequest).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_model_provided_for_required_harness_does_not_warn()
    {
        TestHarnessAdapter adapter = new("TestAgent", requiresModel: true);
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            (LaunchOutcome outcome, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                flow.ExecuteAsync(
                    new LaunchRequest("TestAgent", null, null, null, "gpt-4", [], null)));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(errorOutput.Contains("requires a model")).IsFalse();
            await Assert.That(adapter.LastRequest).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_model_not_required_does_not_warn()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            DefaultSettings());
        try
        {
            (LaunchOutcome outcome, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                flow.ExecuteAsync(
                    new LaunchRequest("TestAgent", null, null, null, null, [], null)));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(errorOutput.Contains("requires a model")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_codex_launch_injects_key_writes_config_and_restores_on_exit()
    {
        const string originalConfig = "# my codex config\nmodel = \"user-model\"\n";
        string? contentDuringLaunch = null;
        string? envDuringLaunch = null;
        // The runner needs the config path, which the factory returns; seed
        // the capture slot before the call and fill it from the result (the
        // callback runs later, during ExecuteAsync).
        string configPath = string.Empty;
        
        CallbackLaunchRunner runner = new(plan =>
        {
            contentDuringLaunch = File.ReadAllText(configPath);
            // The saved key is carried on the plan's process configuration,
            // scoped to the spawned codex process under the env_key
            // variable's name.
            plan.Process.EnvironmentVariables.TryGetValue(
                CodexAdapter.EnvKeyName,
                out envDuringLaunch);
            return Task.FromResult(0);
        });

        (LaunchFlow flow, string tempDir, string path, string backupsDir) = CreateCodexFlow(
            originalConfig,
            runner: runner,
            apiKey: "sk-codex");
        configPath = path;

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("codex", null, null, null, "gpt-5", [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            // The launch saw the launcher-owned provider block...
            await Assert.That(contentDuringLaunch).IsNotNull();
            await Assert.That(contentDuringLaunch).Contains("[model_providers.harnessgateway]");
            await Assert.That(contentDuringLaunch).Contains("model_provider = \"harnessgateway\"");
            // ...and the saved key injected under the env_key variable's name.
            await Assert.That(envDuringLaunch).IsEqualTo("sk-codex");

            // Without persist-config the original file is restored on exit.
            string finalContent = await File.ReadAllTextAsync(configPath);
            await Assert.That(finalContent).IsEqualTo(originalConfig);
            // No persisted marker is left behind either.
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_codex_interactive_plans_the_daemon_stop_for_the_runner()
    {
        const string originalConfig = "# my codex config\n";
        LaunchPlan? planDuringLaunch = null;
        CallbackLaunchRunner runner = new(plan =>
        {
            planDuringLaunch = plan;
            return Task.FromResult(0);
        });

        (LaunchFlow flow, string tempDir, _, _) = CreateCodexFlow(
            originalConfig,
            runner: runner);

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("codex", null, null, null, "gpt-5", [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            // The plan reaches the runner with its pre-launch step intact:
            // the flow executes the configuration actions and hands the
            // plan over otherwise untouched, so the daemon stop still runs
            // before the spawn it exists to prepare.
            await Assert.That(planDuringLaunch).IsNotNull();
            await Assert.That(planDuringLaunch!.PreLaunchProcess).IsNotNull();
            await Assert.That(planDuringLaunch.PreLaunchProcess!.ArgumentList)
                .IsEquivalentTo(["app-server", "daemon", "stop"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_codex_persist_config_keeps_the_write_and_consumes_the_marker()
    {
        const string originalConfig = "# my codex config\nmodel = \"user-model\"\n";
        // Uppercased to prove agent lookup resolves CommandName case-insensitively.
        (LaunchFlow flow, string tempDir, string configPath, string backupsDir) = CreateCodexFlow(
            originalConfig);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("CODEX", null, null, null, "gpt-5", ["config"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            string content = await File.ReadAllTextAsync(configPath);
            await Assert.That(content).Contains("[model_providers.harnessgateway]");
            await Assert.That(content).Contains("model_provider = \"harnessgateway\"");
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_restricted_protocol_adapter_launches_when_the_pairing_matches()
    {
        TestHarnessAdapter adapter = new(
            "TestAgent",
            supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "openai-backend",
                    BaseUri = new Uri("https://api.openai.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                },
            ],
            DefaultProvider = "openai-backend",
        };

        (LaunchFlow flow, string tempDir, _, string backupsDir) = CreateFlow(
            adapter: adapter,
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest.Launch.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(Directory.GetFiles(backupsDir)).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_codex_self_heal_restores_leftover_backup()
    {
        const string originalConfig = "# original codex config\n";
        const string corrupted = "leftover launch write\n";
        (LaunchFlow flow, string tempDir, string configPath, string backupsDir) = CreateCodexFlow(
            corrupted,
            codexAvailable: false);

        try
        {
            // Simulate a crash: a leftover, un-persisted backup marker for
            // the Codex write.
            BackupRecord record = new(
                configPath,
                System.Text.Encoding.UTF8.GetBytes(originalConfig),
                Persisted: false);
            string leftoverPath = Path.Combine(backupsDir, "leftover.json");
            await File.WriteAllTextAsync(leftoverPath, System.Text.Json.JsonSerializer.Serialize(record));

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("codex", null, null, null, null, [], null));

            // Self-heal runs before anything else, so the original config is
            // back even though the launch itself never reached the plan
            // (harness unavailable).
            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.HarnessUnavailable);
            string content = await File.ReadAllTextAsync(configPath);
            await Assert.That(content).IsEqualTo(originalConfig);
            await Assert.That(File.Exists(leftoverPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_claude_plan_is_env_vars_only_and_registers_no_backups()
    {
        LaunchPlan? planDuringLaunch = null;
        CallbackLaunchRunner runner = new(plan =>
        {
            planDuringLaunch = plan;
            return Task.FromResult(0);
        });

        (LaunchFlow flow, string tempDir, string backupsDir) = CreateClaudeFlow(
            apiKey: "sk-claude",
            runner: runner);

        try
        {
            // Agent lookup resolves the CommandName token case-insensitively.
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("CLAUDE-CODE", null, null, null, "claude-sonnet-5", [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(planDuringLaunch).IsNotNull();

            IReadOnlyDictionary<string, string> env = planDuringLaunch.Process.EnvironmentVariables;
            await Assert.That(env["ANTHROPIC_BASE_URL"]).IsEqualTo("https://api.example.com");
            await Assert.That(env["ANTHROPIC_AUTH_TOKEN"]).IsEqualTo("sk-claude");
            await Assert.That(env["ANTHROPIC_MODEL"]).IsEqualTo("claude-sonnet-5");
            // Token variable choice, locked end to end: never ANTHROPIC_API_KEY.
            await Assert.That(env.ContainsKey("ANTHROPIC_API_KEY")).IsFalse();

            // The credential reaches the harness as the secret; the at-rest
            // reference never appears in any planned environment variable.
            foreach (KeyValuePair<string, string> variable in env)
            {
                await Assert.That(variable.Value.Contains("hgkey:")).IsFalse();
            }

            List<CliArgumentAction> cliArgs =
            [
                .. planDuringLaunch.ConfigurationActions.OfType<CliArgumentAction>()
            ];
            await Assert.That(cliArgs).Count().IsEqualTo(1);
            await Assert.That(cliArgs[0].Argument).IsEqualTo("--model");
            await Assert.That(cliArgs[0].Value).IsEqualTo("claude-sonnet-5");

            // No config file is written, so restore-on-exit is trivially
            // satisfied: no backup marker is ever created.
            await Assert.That(
                planDuringLaunch.ConfigurationActions.OfType<ConfigFileWriteAction>()).IsEmpty();
            await Assert.That(Directory.GetFiles(backupsDir)).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_claude_without_saved_key_omits_token_and_never_touches_the_store()
    {
        LaunchPlan? planDuringLaunch = null;
        CallbackLaunchRunner runner = new(plan =>
        {
            planDuringLaunch = plan;
            return Task.FromResult(0);
        });

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        (LaunchFlow flow, string tempDir, string backupsDir) = CreateClaudeFlow(
            keyStore: keyStore,
            runner: runner);

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("claude-code", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(planDuringLaunch).IsNotNull();

            IReadOnlyDictionary<string, string> env = planDuringLaunch.Process.EnvironmentVariables;
            await Assert.That(env.ContainsKey("ANTHROPIC_BASE_URL")).IsTrue();
            // No saved key: no token variable at all, and no dummy value.
            await Assert.That(env.ContainsKey("ANTHROPIC_AUTH_TOKEN")).IsFalse();
            // No model chosen: no model variable and no --model argument action.
            await Assert.That(env.ContainsKey("ANTHROPIC_MODEL")).IsFalse();
            await Assert.That(planDuringLaunch.ConfigurationActions).IsEmpty();
            await Assert.That(Directory.GetFiles(backupsDir)).IsEmpty();

            // A keyless backend never reaches the store at all.
            await Assert.That(keyStore.RetrieveCallCount).IsEqualTo(0);
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_model_never_writes_the_resolved_secret_into_settings()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend",
                },
            ],
            DefaultProvider = "test-backend",
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:test-backend", "sk-never-on-disk");

        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(
            adapter: adapter,
            settings: settings,
            keyStore: keyStore);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, "gpt-4", ["model"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            string fileBytes = await File.ReadAllTextAsync(
                Path.Combine(settingsDir, "HarnessGateway-launcher.json"));
            // A later Save rewrites the at-rest reference; the
            // resolved secret never lands on disk.
            await Assert.That(fileBytes.Contains("hgkey:")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-never-on-disk")).IsFalse();

            // And the harness saw the secret, never the reference.
            await Assert.That(adapter.LastRequest?.ApiKey).IsEqualTo("sk-never-on-disk");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_wizard_key_on_an_ad_hoc_override_reaches_the_launch_and_writes_nothing()
    {
        // First-run wizard shape: no saved backend at all, so resolution
        // builds the ad-hoc override entry with no key. The key the wizard
        // collected must go with the invocation, and the ad-hoc launch must
        // never create a settings file.
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(adapter: adapter);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "TestAgent",
                    null,
                    "https://api.example.com/v1",
                    "openai-chat",
                    "gpt-4",
                    [],
                    "sk-wizard-only"));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            // The first-run launch arrives as the ad-hoc case - keyless by
            // design - with the wizard's session key alongside it.
            await Assert.That(adapter.LastRequest?.Launch)
                .IsTypeOf<LaunchResolution.AdHocCase>();
            await Assert.That(adapter.LastRequest?.ApiKey).IsEqualTo("sk-wizard-only");

            // Launch-copy-only: an ad-hoc override against an empty settings
            // dir never persists anything, secret or otherwise.
            await Assert.That(File.Exists(
                Path.Combine(settingsDir, "HarnessGateway-launcher.json"))).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_session_key_overrides_the_stored_entry_key_on_the_launch_copy()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend",
                },
            ],
            DefaultProvider = "test-backend",
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:test-backend", "sk-stored");

        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(
            adapter: adapter,
            settings: settings,
            keyStore: keyStore);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null,
                    null, [], "sk-explicit"));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest?.ApiKey).IsEqualTo("sk-explicit");

            // The override is launch-copy-only: the stored entry keeps its
            // reference and the store keeps sk-stored under it.
            string fileBytes = await File.ReadAllTextAsync(
                Path.Combine(settingsDir, "HarnessGateway-launcher.json"));
            await Assert.That(fileBytes.Contains("hgkey:test-backend")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-explicit")).IsFalse();
            await Assert.That(keyStore.Contains("hgkey:test-backend")).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_provider_name_launches_that_entry_with_its_resolved_key()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                },
                new Provider
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiResponses,
                    ApiKey = "hgkey:secondary",
                },
            ],
            DefaultProvider = "primary",
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:secondary", "sk-secondary");

        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            settings: settings,
            keyStore: keyStore);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", "secondary", null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            // The stored entry is carried by the provider case of the payload...
            LaunchResolution.ProviderCase providerCase =
                (LaunchResolution.ProviderCase)adapter.LastRequest.Launch;
            await Assert.That(providerCase.Provider.Name).IsEqualTo("secondary");
            // ...and the selection's stored key is the only credential
            // source, resolved through the store before the adapter ever
            // sees it.
            await Assert.That(adapter.LastRequest.ApiKey).IsEqualTo("sk-secondary");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_provider_with_endpoint_override_replaces_only_that_field()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                },
                new Provider
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiResponses,
                    DefaultModel = "gpt-4o",
                    ApiKey = "hgkey:secondary",
                },
            ],
            DefaultProvider = "primary",
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:secondary", "sk-secondary");

        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            settings: settings,
            keyStore: keyStore);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "TestAgent",
                    "secondary",
                    "https://override.example/v1",
                    "openai-chat",
                    null,
                    [],
                    null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest.Launch.BaseUri)
                .IsEqualTo(new Uri("https://override.example/v1"));
            await Assert.That(adapter.LastRequest.Launch.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            // The flag replaced only its own fields; the rest stays as saved.
            LaunchResolution.ProviderCase providerCase =
                (LaunchResolution.ProviderCase)adapter.LastRequest.Launch;
            await Assert.That(providerCase.Provider.Name).IsEqualTo("secondary");
            await Assert.That(providerCase.Provider.DefaultModel).IsEqualTo("gpt-4o");
            await Assert.That(adapter.LastRequest.ApiKey).IsEqualTo("sk-secondary");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_model_on_a_named_provider_never_repoints_the_default()
    {
        LauncherSettings settings = new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                },
                new Provider
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiResponses,
                },
            ],
            DefaultProvider = "primary",
        };

        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(
            adapter: adapter,
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", "secondary", null, null, "gpt-5", ["model"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            // Persisting the model never re-points DefaultProvider: selecting
            // a backend per launch has no settings side effect beyond the
            // model.
            LauncherSettings saved = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(saved.DefaultProvider).IsEqualTo("primary");
            await Assert.That(saved.Providers.Single(b => b.Name == "secondary").DefaultModel)
                .IsEqualTo("gpt-5");
            await Assert.That(saved.Providers.Single(b => b.Name == "primary").DefaultModel)
                .IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_model_with_raw_url_and_no_entries_writes_nothing()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(adapter: adapter);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "TestAgent", null, "https://raw.example/v1", "openai-chat", "gpt-5", ["model"], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            // No stored entry exists to persist onto, so nothing is written -
            // an ad-hoc override never becomes a settings entry.
            LauncherSettings saved = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(saved.Providers).IsEmpty();
            await Assert.That(saved.DefaultProvider).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_unsupported_schema_version_folds_to_failed_without_partial_state()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, string backupsDir) = CreateFlow(
            adapter: adapter);
        try
        {
            string settingsPath = Path.Combine(settingsDir, "HarnessGateway-launcher.json");
            string rawJson = """{ "backends": [], "defaultBackend": "", "schemaVersion": 4 }""";
            await File.WriteAllTextAsync(settingsPath, rawJson);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.UnsupportedSettingsSchema);
            await Assert.That(failed.Message).Contains("unsupported settings schema version");
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(backupsDir)).IsEmpty();
            await Assert.That(await File.ReadAllTextAsync(settingsPath)).IsEqualTo(rawJson);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_v1_plaintext_settings_migrate_on_the_launch_read_path()
    {
        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, string settingsDir, _) = CreateFlow(
            adapter: adapter,
            keyStore: keyStore);
        try
        {
            string settingsPath = Path.Combine(settingsDir, "HarnessGateway-launcher.json");
            await File.WriteAllTextAsync(
                settingsPath,
                """
                {
                  "backends": [
                    {
                      "name": "default",
                      "baseUri": "https://api.example.com/v1",
                      "protocol": 0,
                      "apiKey": "sk-legacy-plaintext"
                    }
                  ],
                  "defaultBackend": "default",
                  "schemaVersion": 1
                }
                """);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(1);
            await Assert.That(keyStore.Contains("hgkey:default")).IsTrue();

            string fileBytes = await File.ReadAllTextAsync(settingsPath);
            string expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
            await Assert.That(fileBytes.Contains(expectedStamp)).IsTrue();
            await Assert.That(fileBytes.Contains("hgkey:default")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-legacy-plaintext")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_migration_and_config_restore_share_one_backup_service_over_the_production_directory()
    {
        // Production layout and wiring: one backups directory under the
        // settings dir, one BackupService shared by the store's migration
        // and the flow's config-action restore. The migration's commit must
        // be durable without disturbing the restore, and vice versa.
        string tempDir = CreateTempDir();
        try
        {
            string settingsDir = Path.Combine(tempDir, "settings");
            Directory.CreateDirectory(settingsDir);
            string backupsDir = Path.Combine(settingsDir, "backups");
            Directory.CreateDirectory(backupsDir);

            const string originalConfig = "# original config\n";
            string configPath = Path.Combine(tempDir, "harness-config.toml");
            await File.WriteAllTextAsync(configPath, originalConfig);

            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            BackupService backupService = new BackupService(backupsDir);
            SettingsStore settingsStore = new SettingsStore(settingsDir, keyStore, backupService);
            await File.WriteAllTextAsync(
                settingsStore.SettingsFilePath,
                """
                {
                  "backends": [
                    {
                      "name": "work",
                      "baseUri": "https://api.work.example/v1",
                      "protocol": 0
                    }
                  ],
                  "defaultBackend": "work",
                  "schemaVersion": 2
                }
                """);

            LaunchFlow flow = new(
                settingsStore,
                backupService,
                new TestLaunchRunner(),
                [new ConfigWritingAdapter(configPath)],
                keyStore,
                new FakeGuiInstallDiscovery(),
                new FakeRunningInstanceDetector(),
                new FakeConfirmationPrompt(),
                new ProtocolEmissionPlanner(),
                new FakeTerminalProbe());

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("ConfigWriter", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);

            // The migration converged the file, and the launch-time restore
            // (persist = none) did not undo it...
            string settingsBytes = await File.ReadAllTextAsync(settingsStore.SettingsFilePath);
            await Assert.That(settingsBytes.Contains(
                $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}")).IsTrue();
            await Assert.That(settingsBytes.Contains("\"providers\"")).IsTrue();

            // ...while the config-action backup still restored as designed.
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(originalConfig);

            // Both markers are gone: the migration's by commit, the config's
            // by restore-at-exit.
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Launch mode: the terminal gate, -p/--print, and dispatch ---

    [Test]
    public async Task ExecuteAsync_interactive_launch_without_a_terminal_refuses_before_planning()
    {
        // The terminal gate: a CLI harness with no terminal to hand over
        // must refuse - never spawn a child that would render into pipes.
        // The refusal kind is the proof (a launch with no provider would
        // stop as NoProvider instead), and the untouched runner proves
        // nothing was started.
        ModeRecordingLaunchRunner runner = new();
        (LaunchFlow flow, string tempDir, _) = CreateClaudeFlow(
            runner: runner,
            terminalProbe: new FakeTerminalProbe(available: false));

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("claude-code", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.TerminalRequired);
            await Assert.That(runner.InteractiveRunCallCount).IsEqualTo(0);
            await Assert.That(runner.NoTimeoutRunCallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_print_launch_bypasses_the_terminal_gate_and_runs_buffered()
    {
        // -p/--print is the piped-context answer to the gate: with no
        // terminal it still runs, dispatched to the buffered no-timeout
        // member (output echoed at exit), and the prompt reaches the
        // harness's own one-shot spelling on argv.
        ModeRecordingLaunchRunner runner = new();
        (LaunchFlow flow, string tempDir, _) = CreateClaudeFlow(
            runner: runner,
            terminalProbe: new FakeTerminalProbe(available: false));

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "claude-code", null, null, null, null, [], null,
                    Print: true, PrintPrompt: "fix the failing test"));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(runner.NoTimeoutRunCallCount).IsEqualTo(1);
            await Assert.That(runner.InteractiveRunCallCount).IsEqualTo(0);

            await Assert.That(runner.LastPlan).IsNotNull();
            await Assert.That(runner.LastPlan!.Process.ArgumentList)
                .IsEquivalentTo(["-p", "fix the failing test"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_print_launch_without_a_prompt_refuses()
    {
        (LaunchFlow flow, string tempDir, _) = CreateClaudeFlow();

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "claude-code", null, null, null, null, [], null,
                    Print: true, PrintPrompt: null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.PrintPromptMissing);
            await Assert.That(refused.Message).Contains("-p/--print");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_print_against_a_harness_without_a_non_interactive_mode_refuses()
    {
        // TestHarnessAdapter plans only the interactive shape, so -p must
        // stop as a refusal rather than silently launching it interactively.
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(adapter: adapter);

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest(
                    "TestAgent", null, null, null, null, [], null,
                    Print: true, PrintPrompt: "hello"));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind)
                .IsEqualTo(LaunchRefusalKind.PrintUnsupportedForHarness);
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_interactive_cli_launch_dispatches_to_the_interactive_member()
    {
        // The dispatch itself: a terminal session goes to RunInteractiveAsync
        // (console-shared, no clock) and never to the buffered members. The
        // GUI branch and the print branch are pinned by their own tests.
        ModeRecordingLaunchRunner runner = new();
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings("test-backend"),
            runner: runner);

        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(runner.InteractiveRunCallCount).IsEqualTo(1);
            await Assert.That(runner.NoTimeoutRunCallCount).IsEqualTo(0);
            await Assert.That(runner.RunCallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- LaunchRequest shape ---

    [Test]
    public async Task LaunchRequest_carries_no_resolved_secret_beyond_session_key()
    {
        // The request is purely declarative: its fields are all
        // pre-resolution inputs. A Provider - the type that carries the
        // resolved secret - must not be reachable through any of them, and
        // SessionKey is the only secret-adjacent slot (a plain string the
        // flow layers onto the launch copy).
        PropertyInfo[] properties = typeof(LaunchRequest).GetProperties();

        string declaredNames = string.Join(
            ",",
            properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        await Assert.That(declaredNames).IsEqualTo(
            "ConfirmRunning,Endpoint,Harness,Model,Persist,Print,PrintPrompt,Protocol,ProviderName,SessionKey");

        await Assert.That(
            properties.Any(p => typeof(Provider).IsAssignableFrom(p.PropertyType))).IsFalse();

        PropertyInfo sessionKey = typeof(LaunchRequest).GetProperty("SessionKey");
        await Assert.That(sessionKey?.PropertyType).IsEqualTo(typeof(string));
    }

    // --- Constructor fakes ---

    private sealed class TestLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public int Handoff(LaunchPlan plan)
        {
            return 0;
        }
    }

    private sealed class FaultingLaunchRunner(Exception exception) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public int Handoff(LaunchPlan plan)
        {
            throw exception;
        }
    }

    private sealed class CallbackLaunchRunner(Func<LaunchPlan, Task<int>> onRun) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return onRun(plan);
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public int Handoff(LaunchPlan plan)
        {
            return onRun(plan).GetAwaiter().GetResult();
        }
    }

    private sealed class CountingLaunchRunner : ILaunchRunner
    {
        public int RunCallCount { get; private set; }

        private int HandoffCallCount { get; set; }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            RunCallCount++;
            return Task.FromResult(0);
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public int Handoff(LaunchPlan plan)
        {
            HandoffCallCount++;
            return HandoffCallCount;
        }
    }

    /// <summary>
    /// One counter per blocking member plus the plan each was handed: the
    /// mode-dispatch tests need to know which member the flow routed to,
    /// which no single-counter fake can tell.
    /// </summary>
    private sealed class ModeRecordingLaunchRunner : ILaunchRunner
    {
        public int RunCallCount { get; private set; }

        public int NoTimeoutRunCallCount { get; private set; }

        public int InteractiveRunCallCount { get; private set; }

        public LaunchPlan? LastPlan { get; private set; }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            RunCallCount++;
            LastPlan = plan;
            return Task.FromResult(0);
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            NoTimeoutRunCallCount++;
            LastPlan = plan;
            return Task.FromResult(0);
        }

        public Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            InteractiveRunCallCount++;
            LastPlan = plan;
            return Task.FromResult(0);
        }

        public int Handoff(LaunchPlan plan)
        {
            return 0;
        }
    }

    /// <summary>
    /// A store whose retrieve path fails operationally (not the absent-key
    /// miss): the fold must land on CredentialStoreError.
    /// </summary>
    private sealed class FaultingKeyStore : ISecureKeyStore
    {
        public Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
        {
            throw SecureKeyStoreException.SecureOperationFailed(
                "fake-store",
                "the fake store failed on retrieve.");
        }

        public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestHarnessAdapter : IHarnessAdapter
    {
        private readonly bool _isAvailable;
        private readonly bool _requiresModel;

        public HarnessMetadata Metadata { get; }

        public ResolvedLaunch? LastRequest { get; private set; }

        public TestHarnessAdapter(
            string name,
            bool isAvailable = true,
            string installHint = "Install via: test-install",
            IReadOnlySet<ProtocolKind>? supportedProtocols = null,
            bool requiresModel = false)
        {
            _isAvailable = isAvailable;
            _requiresModel = requiresModel;
            Metadata = new HarnessMetadata(
                name,
                name,
                [],
                HarnessKind.Cli,
                requiresModel,
                installHint,
                $"{name} --version",
                [],
                supportedProtocols ?? AllProtocols,
                new HashSet<ConfigurationSurface>(),
                []);
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_isAvailable);
        }

        public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ProcessConfiguration config = new("test")
            {
                Arguments = "run",
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true,
            };
            return Task.FromResult(new LaunchPlan(config, []));
        }
    }

    private sealed class ConfigWritingAdapter(string targetPath) : IHarnessAdapter
    {
        public HarnessMetadata Metadata { get; } = new(
            "ConfigWriter",
            "ConfigWriter",
            [],
            HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "N/A",
            DetectionCommand: "ConfigWriter --version",
            [],
            AllProtocols,
            new HashSet<ConfigurationSurface>(),
            []);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true,
            };

            ConfigurationAction[] actions =
            [
                new ConfigFileWriteAction(targetPath, "{\"modified\":true}"),
            ];

            return Task.FromResult(new LaunchPlan(config, actions));
        }
    }
}
