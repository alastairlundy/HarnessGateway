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
/// Pins the flow interface itself (#T016): the structural restore guarantee,
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
            effectiveKeyStore);
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
            new FakeSecureKeyStore());
        return (flow, tempDir, configFile, settingsDir);
    }

    private static (LaunchFlow flow, string tempDir, string configPath) CreateCodexFlow(string userOwnedConfig)
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

        SettingsStore settingsStore = new(settingsDir, new FakeSecureKeyStore());
        settingsStore.Save(new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "codex-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                },
            ],
            DefaultBackend = "codex-backend",
        });

        CodexAdapter adapter = new(
            (_, _) => Task.FromResult(true),
            name => name == "CODEX_HOME" ? codexHome : null);

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            new TestLaunchRunner(),
            [adapter],
            new FakeSecureKeyStore());
        return (flow, tempDir, configPath);
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
                new FakeSecureKeyStore());

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
            await Assert.That(completed.ExitStatus).IsEqualTo(0);
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
            await Assert.That(completed.ExitStatus).IsEqualTo(0);
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
        (LaunchFlow flow, string tempDir, string configPath) = CreateCodexFlow(userOwnedConfig);
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
            await Assert.That(completed.ExitStatus).IsEqualTo(0);

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
            await Assert.That(completed.ExitStatus).IsEqualTo(0);
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

    // --- LaunchRequest shape ---

    [Test]
    public async Task LaunchRequest_carries_no_resolved_secret_beyond_session_key()
    {
        // The request is purely declarative: its seven fields are all
        // pre-resolution inputs. A Backend - the type that carries the
        // resolved secret - must not be reachable through any of them, and
        // SessionKey is the only secret-adjacent slot (a plain string the
        // flow layers onto the launch copy).
        PropertyInfo[] properties = typeof(LaunchRequest).GetProperties();

        string declaredNames = string.Join(
            ",",
            properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        await Assert.That(declaredNames).IsEqualTo(
            "BackendName,Endpoint,Harness,Model,Persist,Protocol,SessionKey");

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
    }

    private sealed class FaultingLaunchRunner(Exception exception) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
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
    }

    private sealed class CountingLaunchRunner : ILaunchRunner
    {
        public int RunCallCount { get; private set; }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            RunCallCount++;
            return Task.FromResult(0);
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

        public HarnessMetadata Metadata { get; }

        public ResolvedLaunch? LastRequest { get; private set; }

        public TestHarnessAdapter(
            string name,
            bool isAvailable = true,
            string installHint = "Install via: test-install",
            IReadOnlySet<ProtocolKind>? supportedProtocols = null)
        {
            _isAvailable = isAvailable;
            Metadata = new HarnessMetadata(
                name,
                name,
                [],
                HarnessKind.Cli,
                RequiresModel: false,
                installHint,
                DetectionCommand: $"{name} --version",
                [],
                supportedProtocols ?? AllProtocols);
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
            AllProtocols);

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
