using System.Reflection;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
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
        ISecureKeyStore? keyStore = null)
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
            new FakeConfirmationPrompt());
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
            new FakeConfirmationPrompt());
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
            Backends =
            [
                new Backend
                {
                    Name = "codex-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                    ApiKey = keyReference
                },
            ],
            DefaultBackend = "codex-backend",
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
            new FakeConfirmationPrompt());
        return (flow, tempDir, configPath, backupsDir);
    }

    private static (LaunchFlow flow, string tempDir, string backupsDir) CreateClaudeFlow(
        bool available = true,
        string? apiKey = null,
        FakeSecureKeyStore? keyStore = null,
        ILaunchRunner? runner = null)
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
            Backends =
            [
                new Backend
                {
                    Name = "claude-backend",
                    BaseUri = new Uri("https://api.example.com"),
                    Protocol = ProtocolKind.Anthropic,
                    ApiKey = keyReference
                },
            ],
            DefaultBackend = "claude-backend",
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
            new FakeConfirmationPrompt());
        return (flow, tempDir, backupsDir);
    }

    /// <summary>
    /// Settings fixtures only ever carry the reference; the secret itself is
    /// seeded into the fake store the way the real store would hold it.
    /// </summary>
    private static string? SeedKey(FakeSecureKeyStore keyStore, string backendName, string? secret)
    {
        if (secret is null)
        {
            return null;
        }

        string reference = KeyReference.ForBackend(backendName);
        keyStore.Seed(reference, secret);
        return reference;
    }

    private static LauncherSettings DefaultSettings(string name = "default") => new()
    {
        Backends =
        [
            new Backend
            {
                Name = name,
                BaseUri = new Uri("https://api.default.com/v1"),
                Protocol = ProtocolKind.OpenAiChat,
            },
        ],
        DefaultBackend = name,
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

            // The headline pin: the fault does not leak as an exception, and
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
                new FakeConfirmationPrompt());

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
            settings: DefaultSettings());
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

            // The opt-out is honored on the fault path too: the write is
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
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                },
            ],
            DefaultBackend = "default",
        };

        // Reference present, secret absent from the store.
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
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
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default",
                },
            ],
            DefaultBackend = "default",
        };

        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
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
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-session");

            // ...and the --persist model save never does: the settings file
            // keeps its reference/null key shape, never launch-time material.
            string settingsBytes = await File.ReadAllTextAsync(
                Path.Combine(settingsDir, "HarnessGateway-launcher.json"));
            await Assert.That(settingsBytes.Contains("sk-session")).IsFalse();

            LauncherSettings saved = new SettingsStore(settingsDir).Load();
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-5");
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
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings());
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
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings());
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
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings());
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
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings());
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
    public async Task ExecuteAsync_unknown_backend_name_folds_to_refusal()
    {
        LauncherSettings settings = DefaultSettings("primary");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", "nope", null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.UnknownBackendName);
            await Assert.That(refused.Message).Contains("unknown backend name 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_dangling_default_backend_folds_to_refusal()
    {
        LauncherSettings settings = DefaultSettings("primary") with { DefaultBackend = "ghost" };
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.DanglingDefaultBackend);
            await Assert.That(refused.Message).Contains("config set defaultBackend");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_absent_default_backend_folds_to_refusal()
    {
        // DefaultBackend intentionally unset: multiple entries must not fall
        // back to the first one - the pointer is the only read.
        LauncherSettings settings = new()
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                },
            ],
        };

        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: settings);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.AbsentDefaultBackend);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_empty_settings_folds_to_no_backend()
    {
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: new TestHarnessAdapter("TestAgent"));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.NoBackend);
            // The usage-error wording is contractual; no "Launch failed:"
            // prefix may borrow the internal-fault marker.
            await Assert.That(refused.Message).Contains("No backend is configured");
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
            adapter: new TestHarnessAdapter("TestAgent"),
            settings: DefaultSettings());
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
            settings: DefaultSettings());
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
            Backends =
            [
                new Backend
                {
                    Name = "anthropic-backend",
                    BaseUri = new Uri("https://api.anthropic.com"),
                    Protocol = ProtocolKind.Anthropic,
                },
            ],
            DefaultBackend = "anthropic-backend",
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
            LauncherSettings saved = new SettingsStore(settingsDir).Load();
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-5");
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
            LauncherSettings saved = new SettingsStore(settingsDir).Load();
            await Assert.That(saved.Backends[0].DefaultModel).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- End-to-end scenarios migrated from the orchestrator suite:
    //     same coverage, re-pointed onto the flow; assertions now speak the
    //     outcome vocabulary, never exit codes. ---

    [Test]
    public async Task ExecuteAsync_endpoint_and_protocol_override_builds_an_ad_hoc_backend()
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
            await Assert.That(adapter.LastRequest!.Backend.BaseUri)
                .IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_without_overrides_uses_the_default_backend()
    {
        TestHarnessAdapter adapter = new("TestAgent");
        (LaunchFlow flow, string tempDir, _, _) = CreateFlow(
            adapter: adapter,
            settings: DefaultSettings());
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.Name).IsEqualTo("default");
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
            settings: DefaultSettings());
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
            settings: DefaultSettings());
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
            settings: DefaultSettings());
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
            // The saved key rides on the plan's process configuration,
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
            await Assert.That(contentDuringLaunch!).Contains("[model_providers.harnessgateway]");
            await Assert.That(contentDuringLaunch!).Contains("model_provider = \"harnessgateway\"");
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
            // Persisting consumed the backup marker.
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
        // A restricted fake harness that only speaks openai-chat, pointed at
        // a matching backend: compatible pairings launch.
        TestHarnessAdapter adapter = new(
            "TestAgent",
            supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
        LauncherSettings settings = new()
        {
            Backends =
            [
                new Backend
                {
                    Name = "openai-backend",
                    BaseUri = new Uri("https://api.openai.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                },
            ],
            DefaultBackend = "openai-backend",
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
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
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

            IReadOnlyDictionary<string, string> env = planDuringLaunch!.Process.EnvironmentVariables;
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

            IReadOnlyDictionary<string, string> env = planDuringLaunch!.Process.EnvironmentVariables;
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
            Backends =
            [
                new Backend
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend",
                },
            ],
            DefaultBackend = "test-backend",
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
            // The at-rest reference is what a later Save rewrites; the
            // resolved secret never lands on disk.
            await Assert.That(fileBytes.Contains("hgkey:")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-never-on-disk")).IsFalse();

            // And the harness saw the secret, never the reference.
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-never-on-disk");
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
        // collected must ride the invocation - this is the regression the
        // silent-fail bug shipped - and the ad-hoc launch must never create
        // a settings file.
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
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-wizard-only");

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
            Backends =
            [
                new Backend
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend",
                },
            ],
            DefaultBackend = "test-backend",
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
                new LaunchRequest("TestAgent", null, null, null, null, [], "sk-explicit"));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(ExitStatusOf(completed)).IsEqualTo(0);
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-explicit");

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
    public async Task ExecuteAsync_backend_name_launches_that_entry_with_its_resolved_key()
    {
        LauncherSettings settings = new()
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                    ApiKey = "hgkey:secondary",
                },
            ],
            DefaultBackend = "primary",
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
            await Assert.That(adapter.LastRequest!.Backend.Name).IsEqualTo("secondary");
            // The selection's stored key is the only credential source,
            // resolved through the store before the adapter ever sees it.
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-secondary");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_backend_with_endpoint_override_replaces_only_that_field()
    {
        LauncherSettings settings = new()
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                    DefaultModel = "gpt-4o",
                    ApiKey = "hgkey:secondary",
                },
            ],
            DefaultBackend = "primary",
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
            await Assert.That(adapter.LastRequest!.Backend.BaseUri)
                .IsEqualTo(new Uri("https://override.example/v1"));
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            // The flag replaced only its own fields; the rest stays as saved.
            await Assert.That(adapter.LastRequest!.Backend.Name).IsEqualTo("secondary");
            await Assert.That(adapter.LastRequest!.Backend.DefaultModel).IsEqualTo("gpt-4o");
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-secondary");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_persist_model_on_a_named_backend_never_repoints_the_default()
    {
        LauncherSettings settings = new()
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                },
            ],
            DefaultBackend = "primary",
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

            // Persisting the model never re-points DefaultBackend: selecting
            // a backend per launch has no settings side effect beyond the
            // model.
            LauncherSettings saved = new SettingsStore(settingsDir).Load();
            await Assert.That(saved.DefaultBackend).IsEqualTo("primary");
            await Assert.That(saved.Backends.Single(b => b.Name == "secondary").DefaultModel)
                .IsEqualTo("gpt-5");
            await Assert.That(saved.Backends.Single(b => b.Name == "primary").DefaultModel)
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
            LauncherSettings saved = new SettingsStore(settingsDir).Load();
            await Assert.That(saved.Backends).IsEmpty();
            await Assert.That(saved.DefaultBackend).IsEqualTo(string.Empty);
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
            string rawJson = """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""";
            await File.WriteAllTextAsync(settingsPath, rawJson);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null));

            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.UnsupportedSettingsSchema);
            await Assert.That(failed.Message).Contains("unsupported settings schema version");
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(backupsDir)).IsEmpty();
            await Assert.That(File.ReadAllText(settingsPath)).IsEqualTo(rawJson);
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

    // --- LaunchRequest shape ---

    [Test]
    public async Task LaunchRequest_carries_no_resolved_secret_beyond_session_key()
    {
        // The request is purely declarative: its fields are all
        // pre-resolution inputs. A Backend - the type that carries the
        // resolved secret - must not be reachable through any of them, and
        // SessionKey is the only secret-adjacent slot (a plain string the
        // flow layers onto the launch copy).
        PropertyInfo[] properties = typeof(LaunchRequest).GetProperties();

        string declaredNames = string.Join(
            ",",
            properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        await Assert.That(declaredNames).IsEqualTo(
            "BackendName,ConfirmRunning,Endpoint,Harness,Model,Persist,Protocol,SessionKey");

        await Assert.That(
            properties.Any(p => typeof(Backend).IsAssignableFrom(p.PropertyType))).IsFalse();

        PropertyInfo sessionKey = typeof(LaunchRequest).GetProperty("SessionKey")!;
        await Assert.That(sessionKey.PropertyType).IsEqualTo(typeof(string));
    }

    // --- Constructor fakes ---

    private sealed class TestLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
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

        public int Handoff(LaunchPlan plan)
        {
            return onRun(plan).GetAwaiter().GetResult();
        }
    }

    private sealed class CountingLaunchRunner : ILaunchRunner
    {
        public int RunCallCount { get; private set; }

        public int HandoffCallCount { get; private set; }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            RunCallCount++;
            return Task.FromResult(0);
        }

        public int Handoff(LaunchPlan plan)
        {
            HandoffCallCount++;
            return HandoffCallCount;
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
                DetectionCommand: $"{name} --version",
                [],
                supportedProtocols ?? AllProtocols,
                SupportedSurfaces: new HashSet<ConfigurationSurface>(),
                InstanceProcessNames: []);
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
