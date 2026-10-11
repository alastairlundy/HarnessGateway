using CliInvoke.Core;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

public class LaunchOrchestratorTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (LaunchOrchestrator orchestrator, string tempDir) CreateOrchestrator(
        IHarnessAdapter? adapter = null,
        LauncherSettings? settings = null,
        ILaunchRunner? runner = null,
        FakeSecureKeyStore? keyStore = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        SettingsStore settingsStore = new SettingsStore(settingsDir, effectiveKeyStore);
        if (settings is not null)
        {
            settingsStore.Save(settings);
        }

        BackupService backupService = new BackupService(backupsDir);
        ILaunchRunner effectiveRunner = runner ?? new TestLaunchRunner();
        IHarnessAdapter[] adapters = adapter is not null ? [adapter] : [];

        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            settingsStore, backupService, effectiveRunner, adapters, effectiveKeyStore);
        return (orchestrator, tempDir);
    }

    private static TestHarnessAdapter CreateTestAdapter(
        string name = "TestAgent",
        bool isAvailable = true,
        bool requiresModel = false,
        string installHint = "Install via: test-install",
        IReadOnlySet<ProtocolKind>? supportedProtocols = null)
    {
        return new TestHarnessAdapter(name, isAvailable, requiresModel, installHint, supportedProtocols);
    }

    private static (LaunchOrchestrator orchestrator, string tempDir, string codexHome, string configPath, string backupsDir)
        CreateCodexOrchestrator(
            string? originalConfig = null,
            bool codexAvailable = true,
            string? apiKey = null,
            ILaunchRunner? runner = null,
            ProtocolKind backendProtocol = ProtocolKind.OpenAiResponses,
            FakeSecureKeyStore? keyStore = null)
    {
        string tempDir = CreateTempDir();
        string codexHome = Path.Combine(tempDir, "codex-home");
        Directory.CreateDirectory(codexHome);
        string configPath = Path.Combine(codexHome, "config.toml");
        if (originalConfig is not null)
        {
            File.WriteAllText(configPath, originalConfig);
        }

        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        string? keyReference = SeedKey(effectiveKeyStore, "codex-backend", apiKey);

        SettingsStore settingsStore = new SettingsStore(settingsDir, effectiveKeyStore);
        settingsStore.Save(new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "codex-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = backendProtocol,
                    ApiKey = keyReference
                }
            ],
            DefaultBackend = "codex-backend"
        });

        CodexAdapter adapter = new CodexAdapter(
            (_, _) => Task.FromResult(codexAvailable),
            name => name == "CODEX_HOME" ? codexHome : null);

        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            [adapter],
            effectiveKeyStore);
        return (orchestrator, tempDir, codexHome, configPath, backupsDir);
    }

    private static (LaunchOrchestrator orchestrator, string tempDir, string backupsDir)
        CreateClaudeOrchestrator(
            bool available = true,
            string? apiKey = null,
            ILaunchRunner? runner = null,
            FakeSecureKeyStore? keyStore = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        string? keyReference = SeedKey(effectiveKeyStore, "claude-backend", apiKey);

        SettingsStore settingsStore = new SettingsStore(settingsDir, effectiveKeyStore);
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
                }
            ],
            DefaultBackend = "claude-backend"
        });

        // Fake detection delegate: availability never touches the real PATH.
        ClaudeCodeAdapter adapter = new ClaudeCodeAdapter(
            (_, _) => Task.FromResult(available));

        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            [adapter],
            effectiveKeyStore);
        return (orchestrator, tempDir, backupsDir);
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

    [Test]
    public async Task RunAsync_unknown_agent_returns_exit_code_3()
    {
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator();
        try
        {
            // Wrapped in the capture gate: the "Unknown agent" stderr write must
            // not bleed into whoever holds Console.Error redirected in parallel.
            (int exitCode, _) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync(
                    "NonExistentAgent",
                    null,
                    null, null, null, [],
                    tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_unavailable_harness_returns_exit_code_4()
    {
        TestHarnessAdapter adapter = CreateTestAdapter(isAvailable: false, installHint: "Install via: npm install -g test");
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.HarnessUnavailable);
            await Assert.That(errorOutput).Contains("Install via: npm install -g test");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_available_agent_runs_successfully()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_without_configured_backend_refuses()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            // A missing backend is a usage error. It exited 7 before, which is
            // the code for credential-store failures and unhandled exceptions,
            // so a first-run mistake read as a config-restore failure.
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("No backend is configured");

            // The "Launch failed:" prefix is reserved for the unhandled-exception
            // path; a user-rectifiable usage error must not borrow it.
            await Assert.That(errorOutput).DoesNotContain("Launch failed:");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_with_endpoint_and_protocol_uses_override()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                "https://api.example.com/v1",
                "openai-chat",
                null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_without_flags_uses_default_backend()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.Name).IsEqualTo("default");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_warns_and_launches_when_model_required_but_missing()
    {
        TestHarnessAdapter adapter = CreateTestAdapter(requiresModel: true);
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
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
    public async Task RunAsync_does_not_warn_when_model_provided_for_required_harness()
    {
        TestHarnessAdapter adapter = CreateTestAdapter(requiresModel: true);
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, "gpt-4", [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(errorOutput.Contains("requires a model")).IsFalse();
            await Assert.That(adapter.LastRequest).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_does_not_warn_when_model_not_required()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(errorOutput.Contains("requires a model")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_persist_model_saves_to_settings()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "test-backend"
        };

        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, "gpt-4",
                ["model"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            string settingsDir = Path.Combine(tempDir, "settings");
            SettingsStore settingsStore = new SettingsStore(settingsDir);
            LauncherSettings savedSettings = settingsStore.Load();
            await Assert.That(savedSettings.Backends[0].DefaultModel).IsEqualTo("gpt-4");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_persist_config_does_not_restore()
    {
        string tempDir = CreateTempDir();
        try
        {
            string settingsDir = Path.Combine(tempDir, "settings");
            Directory.CreateDirectory(settingsDir);
            string backupsDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupsDir);

            string configFile = Path.Combine(tempDir, "config.json");
            await File.WriteAllTextAsync(configFile, "{}");

            SettingsStore settingsStore = new SettingsStore(settingsDir);
            settingsStore.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.default.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "default"
            });
            BackupService backupService = new BackupService(backupsDir);
            TestLaunchRunner runner = new TestLaunchRunner();
            ConfigWritingAdapter adapter = new ConfigWritingAdapter(configFile);
            LaunchOrchestrator orchestrator = new LaunchOrchestrator(
                settingsStore, backupService, runner, [adapter], new FakeSecureKeyStore());

            int exitCode = await orchestrator.RunAsync(
                "ConfigWriter",
                null,
                null, null, null,
                ["config"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            string content = await File.ReadAllTextAsync(configFile);
            await Assert.That(content).IsEqualTo("{\"modified\":true}");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static readonly HashSet<ProtocolKind> AllProtocols =
    [
        ProtocolKind.OpenAiChat,
        ProtocolKind.OpenAiResponses,
        ProtocolKind.Anthropic
    ];

    [Test]
    public async Task RunAsync_codex_launch_writes_config_then_restores_on_exit()
    {
        const string originalConfig = "# my codex config\nmodel = \"user-model\"\n";
        string configPath = string.Empty;
        string? contentDuringLaunch = null;
        string? envDuringLaunch = null;
        CallbackLaunchRunner runner = new CallbackLaunchRunner(plan =>
        {
            contentDuringLaunch = File.ReadAllText(configPath);
            // The saved key rides on the plan's process configuration, scoped
            // to the spawned codex process under the env_key variable's name.
            plan.Process.EnvironmentVariables.TryGetValue(
                CodexAdapter.EnvKeyName,
                out envDuringLaunch);
            return Task.FromResult(0);
        });

        (LaunchOrchestrator orchestrator, string tempDir, _, string path, _) = CreateCodexOrchestrator(
            originalConfig: originalConfig,
            apiKey: "sk-codex",
            runner: runner);
        configPath = path;

        try
        {
            int exitCode = await orchestrator.RunAsync(
                "codex",
                null,
                null, null, "gpt-5", [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            // The launch saw the launcher-owned provider block...
            await Assert.That(contentDuringLaunch).IsNotNull();
            await Assert.That(contentDuringLaunch!).Contains("[model_providers.harnessgateway]");
            await Assert.That(contentDuringLaunch!).Contains("model_provider = \"harnessgateway\"");
            // ...and the saved key injected under the env_key variable's name.
            await Assert.That(envDuringLaunch).IsEqualTo("sk-codex");

            // Without --persist config the original file is restored on exit.
            string finalContent = await File.ReadAllTextAsync(configPath);
            await Assert.That(finalContent).IsEqualTo(originalConfig);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_codex_persist_config_keeps_the_write()
    {
        const string originalConfig = "# my codex config\nmodel = \"user-model\"\n";
        (LaunchOrchestrator orchestrator, string tempDir, _, string configPath, string backupsDir) = CreateCodexOrchestrator(
            originalConfig: originalConfig);
        try
        {
            // Uppercased to prove agent lookup resolves Metadata.Name case-insensitively.
            int exitCode = await orchestrator.RunAsync(
                "CODEX",
                null,
                null, null, "gpt-5",
                ["config"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

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
    public async Task RunAsync_codex_collision_fails_before_any_write()
    {
        const string userOwnedConfig =
            "# my provider block\n"
            + "[model_providers.harnessgateway]\n"
            + "name = \"Mine\"\n"
            + "base_url = \"https://mine.example/v1\"\n"
            + "env_key = \"MINE_KEY\"\n"
            + "wire_api = \"responses\"\n";
        (LaunchOrchestrator orchestrator, string tempDir, _, string configPath, string backupsDir) = CreateCodexOrchestrator(
            originalConfig: userOwnedConfig);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "codex",
                null,
                null, null, "gpt-5", [],
                tempDir);

            // The adapter refuses during planning, so the launch fails and no write
            // (and therefore no backup marker) ever happens. The refusal is its own
            // exit class, not a restore failure.
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigConflict);
            string content = await File.ReadAllTextAsync(configPath);
            await Assert.That(content).IsEqualTo(userOwnedConfig);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_unsupported_backend_protocol_fails_before_any_write()
    {
        const string originalConfig = "# my codex config\nmodel = \"user-model\"\n";
        (LaunchOrchestrator orchestrator, string tempDir, _, string configPath, string backupsDir) = CreateCodexOrchestrator(
            originalConfig: originalConfig,
            backendProtocol: ProtocolKind.Anthropic);

        try
        {
            int exitCode = await orchestrator.RunAsync(
                "codex",
                null,
                null, null, "gpt-5", [],
                tempDir);

            // Protocol compatibility is enforced by the launch path, before any
            // configuration action, using the unknown-agent/invalid-selection exit code.
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
            string content = await File.ReadAllTextAsync(configPath);
            await Assert.That(content).IsEqualTo(originalConfig);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_incompatible_pairing_fails_with_exit_3_before_any_write()
    {
        // Fake harness that only speaks openai-chat, pointed at an anthropic backend.
        TestHarnessAdapter adapter = CreateTestAdapter(
            supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "anthropic-backend",
                    BaseUri = new Uri("https://api.anthropic.com"),
                    Protocol = ProtocolKind.Anthropic
                }
            ],
            DefaultBackend = "anthropic-backend"
        };

        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
            // Refused before planning, so no configuration action ever ran:
            // the adapter never saw a request and no backup marker exists.
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"))).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_compatible_pairing_with_restricted_adapter_still_launches()
    {
        // Same restricted fake harness, but the backend protocol matches.
        TestHarnessAdapter adapter = CreateTestAdapter(
            supportedProtocols: new HashSet<ProtocolKind> { ProtocolKind.OpenAiChat });
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "openai-backend",
                    BaseUri = new Uri("https://api.openai.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "openai-backend"
        };

        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"))).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_codex_self_heal_restores_leftover_backup()
    {
        const string originalConfig = "# original codex config\n";
        const string corrupted = "leftover launch write\n";
        (LaunchOrchestrator orchestrator, string tempDir, _, string configPath, string backupsDir) = CreateCodexOrchestrator(
            originalConfig: corrupted,
            codexAvailable: false);

        try
        {
            // Simulate a crash: a leftover, un-persisted backup marker for the Codex write.
            BackupRecord record = new BackupRecord(
                configPath,
                System.Text.Encoding.UTF8.GetBytes(originalConfig),
                Persisted: false);
            string leftoverPath = Path.Combine(backupsDir, "leftover.json");
            await File.WriteAllTextAsync(leftoverPath, System.Text.Json.JsonSerializer.Serialize(record));

            int exitCode = await orchestrator.RunAsync(
                "codex",
                null,
                null, null, null, [],
                tempDir);

            // Self-heal runs before anything else, so the original config is back even
            // though the launch itself never reached the plan (harness unavailable).
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.HarnessUnavailable);
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
    public async Task RunAsync_claude_code_launch_plans_env_vars_only_and_creates_no_backups()
    {
        LaunchPlan? planDuringLaunch = null;
        CallbackLaunchRunner runner = new CallbackLaunchRunner(plan =>
        {
            planDuringLaunch = plan;
            return Task.FromResult(0);
        });

        (LaunchOrchestrator orchestrator, string tempDir, string backupsDir) = CreateClaudeOrchestrator(
            apiKey: "sk-claude",
            runner: runner);

        try
        {
            // Agent lookup resolves Metadata.Name case-insensitively.
            int exitCode = await orchestrator.RunAsync(
                "CLAUDE-CODE",
                null,
                null, null, "claude-sonnet-5", [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
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
                .. planDuringLaunch.ConfigurationActions
                    .OfType<CliArgumentAction>()
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
    public async Task RunAsync_claude_code_without_saved_key_omits_token_and_still_launches()
    {
        LaunchPlan? planDuringLaunch = null;
        CallbackLaunchRunner runner = new CallbackLaunchRunner(plan =>
        {
            planDuringLaunch = plan;
            return Task.FromResult(0);
        });

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        (LaunchOrchestrator orchestrator, string tempDir, string backupsDir) = CreateClaudeOrchestrator(
            runner: runner,
            keyStore: keyStore);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "claude-code",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
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
    public async Task RunAsync_dangling_reference_fails_with_exit_7()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:default"
                }
            ],
            DefaultBackend = "default"
        };

        // Reference present, secret absent from the store.
        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, keyStore: keyStore);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            // The store throws AbsentKey at its source; the resolver no longer wraps it.
            await Assert.That(errorOutput).Contains("No stored API key found");
            await Assert.That(errorOutput).Contains("hgkey:default");
            // Failed before planning, so no harness ever saw a request.
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_settings_saved_after_launch_never_contain_the_secret()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend"
                }
            ],
            DefaultBackend = "test-backend"
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:test-backend", "sk-never-on-disk");

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, keyStore: keyStore);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, "gpt-4",
                ["model"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            string fileBytes = await File.ReadAllTextAsync(
                Path.Combine(tempDir, "settings", "HarnessGateway-launcher.json"));
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
    public async Task RunAsync_wizard_api_key_reaches_the_launch_plan()
    {
        // First-run wizard flow: no saved backend at all, so resolution builds
        // the ad-hoc "override" entry with ApiKey = null. The key the wizard
        // collected must ride the invocation - this is the regression the
        // silent-fail bug shipped: the launch went out with no credential and
        // the harness failed auth downstream.
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                "https://api.example.com/v1", "openai-chat", "gpt-4",
                [],
                tempDir,
                apiKey: "sk-wizard-only");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-wizard-only");

            // And launch-copy-only: an ad-hoc override against an empty
            // settings dir never persists anything, secret or otherwise.
            await Assert.That(File.Exists(
                Path.Combine(tempDir, "settings", "HarnessGateway-launcher.json"))).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_explicit_api_key_overrides_the_stored_entry_key()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                    ApiKey = "hgkey:test-backend"
                }
            ],
            DefaultBackend = "test-backend"
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:test-backend", "sk-stored");

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, keyStore: keyStore);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null,
                [],
                tempDir,
                apiKey: "sk-explicit");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-explicit");

            // The override is launch-copy-only: the stored entry keeps its
            // reference and the store keeps sk-stored under it.
            string fileBytes = await File.ReadAllTextAsync(
                Path.Combine(tempDir, "settings", "HarnessGateway-launcher.json"));
            await Assert.That(fileBytes.Contains("hgkey:test-backend")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-explicit")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_with_backend_name_launches_that_entry()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                    ApiKey = "hgkey:secondary"
                }
            ],
            DefaultBackend = "primary"
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:secondary", "sk-secondary");

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, keyStore: keyStore);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                "secondary",
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.Name).IsEqualTo("secondary");
            // The selection's stored key is the only credential source, resolved
            // through the store before the adapter ever sees it.
            await Assert.That(adapter.LastRequest!.Backend.ApiKey).IsEqualTo("sk-secondary");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_with_backend_and_endpoint_overrides_only_that_field()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses,
                    DefaultModel = "gpt-4o",
                    ApiKey = "hgkey:secondary"
                }
            ],
            DefaultBackend = "primary"
        };

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        keyStore.Seed("hgkey:secondary", "sk-secondary");

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, keyStore: keyStore);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                "secondary",
                "https://override.example/v1",
                "openai-chat",
                null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
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
    public async Task RunAsync_unknown_backend_name_returns_exit_code_8()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "primary"
        };

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                "nope",
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            // Refused during resolution, before self-heal, planning or any write.
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"))).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_dangling_default_backend_returns_exit_8_pointing_at_pointer_write()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "ghost"
        };

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("config set defaultBackend");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_absent_default_with_entries_returns_exit_code_8()
    {
        // DefaultBackend intentionally unset: multiple entries must not fall
        // back to the first one - the pointer is the only read.
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses
                }
            ]
        };

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null, null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_protocol_without_endpoint_returns_usage_error()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null, null, "openai-chat", null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_backend_persist_model_updates_only_the_entry_never_the_pointer()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "primary",
                    BaseUri = new Uri("https://primary.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                },
                new Backend
                {
                    Name = "secondary",
                    BaseUri = new Uri("https://secondary.example/v1"),
                    Protocol = ProtocolKind.OpenAiResponses
                }
            ],
            DefaultBackend = "primary"
        };

        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                "secondary",
                null, null, "gpt-5",
                ["model"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            // Persisting the model never re-points DefaultBackend: selecting a
            // backend per launch has no settings side effect beyond the model.
            LauncherSettings savedSettings = new SettingsStore(Path.Combine(tempDir, "settings")).Load();
            await Assert.That(savedSettings.DefaultBackend).IsEqualTo("primary");
            await Assert.That(savedSettings.Backends.Single(b => b.Name == "secondary").DefaultModel)
                .IsEqualTo("gpt-5");
            await Assert.That(savedSettings.Backends.Single(b => b.Name == "primary").DefaultModel)
                .IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_persist_model_with_raw_url_and_no_entries_writes_nothing()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                "https://raw.example/v1",
                "openai-chat",
                "gpt-5",
                ["model"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            // No stored entry exists to persist onto, so nothing is written -
            // an ad-hoc override never becomes a settings entry.
            LauncherSettings savedSettings = new SettingsStore(Path.Combine(tempDir, "settings")).Load();
            await Assert.That(savedSettings.Backends).IsEmpty();
            await Assert.That(savedSettings.DefaultBackend).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_unsupported_schema_version_returns_exit_9_with_no_partial_state()
    {
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            string settingsPath = Path.Combine(tempDir, "settings", "HarnessGateway-launcher.json");
            string rawJson = """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""";
            await File.WriteAllTextAsync(settingsPath, rawJson);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("TestAgent", null, null, null, null, [], tempDir));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnsupportedSettingsSchema);
            await Assert.That(errorOutput).Contains("unsupported settings schema version");
            await Assert.That(adapter.LastRequest).IsNull();
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"))).IsEmpty();
            await Assert.That(File.ReadAllText(settingsPath)).IsEqualTo(rawJson);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_migrates_a_v1_plaintext_file_on_the_launch_read_path()
    {
        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        TestHarnessAdapter adapter = CreateTestAdapter();
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, keyStore: keyStore);
        try
        {
            string settingsPath = Path.Combine(tempDir, "settings", "HarnessGateway-launcher.json");
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

            int exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null,
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
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

    private class TestLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private class CallbackLaunchRunner : ILaunchRunner
    {
        private readonly Func<LaunchPlan, Task<int>> _onRun;

        public CallbackLaunchRunner(Func<LaunchPlan, Task<int>> onRun)
        {
            _onRun = onRun;
        }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return _onRun(plan);
        }
    }

    private class TestHarnessAdapter : IHarnessAdapter
    {
        private readonly bool _isAvailable;

        public HarnessMetadata Metadata { get; }
        public LaunchRequest? LastRequest { get; private set; }

        public TestHarnessAdapter(
            string name,
            bool isAvailable,
            bool requiresModel,
            string installHint,
            IReadOnlySet<ProtocolKind>? supportedProtocols = null,
            string? commandName = null)
        {
            _isAvailable = isAvailable;
            Metadata = new HarnessMetadata(
                name,
                // Defaults to the label, which is what every existing call site
                // means. Pass commandName to model a harness whose display name
                // and launch token differ, as Copilot's do.
                commandName ?? name,
                [],
                HarnessKind.Cli,
                requiresModel,
                installHint,
                DetectionCommand: $"{name} --version",
                [],
                supportedProtocols ?? AllProtocols);
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_isAvailable);
        }

        public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ProcessConfiguration config = new ProcessConfiguration("test")
            {
                Arguments = "run",
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new LaunchPlan(config, []));
        }
    }

    private class ConfigWritingAdapter : IHarnessAdapter
    {
        private readonly string _configFile;

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

        public ConfigWritingAdapter(string configFile)
        {
            _configFile = configFile;
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new ProcessConfiguration("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };

            ConfigurationAction[] actions =
            [
                new ConfigFileWriteAction(_configFile, "{\"modified\":true}")
            ];

            return Task.FromResult(new LaunchPlan(config, actions));
        }
    }
}
