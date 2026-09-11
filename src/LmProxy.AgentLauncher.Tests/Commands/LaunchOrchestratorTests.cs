using CliInvoke.Core;
using TUnit;
using LmProxy.AgentLauncher.Commands;
using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Execution;
using LmProxy.AgentLauncher.Core.Launch;
using LmProxy.AgentLauncher.Core.Settings;

namespace LmProxy.AgentLauncher.Tests.Commands;

public class LaunchOrchestratorTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lmproxy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (LaunchOrchestrator orchestrator, string tempDir) CreateOrchestrator(
        IHarnessAdapter? adapter = null,
        LauncherSettings? settings = null,
        ILaunchRunner? runner = null)
    {
        var tempDir = CreateTempDir();
        var settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        var backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        var settingsStore = new SettingsStore(settingsDir);
        if (settings is not null)
        {
            settingsStore.Save(settings);
        }

        var backupService = new BackupService(backupsDir);
        var effectiveRunner = runner ?? new TestLaunchRunner();
        var adapters = adapter is not null ? new[] { adapter } : [];

        var orchestrator = new LaunchOrchestrator(settingsStore, backupService, effectiveRunner, adapters);
        return (orchestrator, tempDir);
    }

    private static TestHarnessAdapter CreateTestAdapter(
        string name = "TestAgent",
        bool isAvailable = true,
        bool requiresModel = false,
        string installHint = "Install via: test-install")
    {
        return new TestHarnessAdapter(name, isAvailable, requiresModel, installHint);
    }

    [Test]
    public async Task RunAsync_unknown_agent_returns_exit_code_3()
    {
        var (orchestrator, tempDir) = CreateOrchestrator();
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "NonExistentAgent",
                null, null, null, [],
                tempDir);

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
        var adapter = CreateTestAdapter(isAvailable: false);
        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null, null, null, [],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.HarnessUnavailable);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_available_agent_runs_successfully()
    {
        var adapter = CreateTestAdapter();
        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
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
    public async Task RunAsync_with_endpoint_and_protocol_uses_override()
    {
        var adapter = CreateTestAdapter();
        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
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
        var settings = new LauncherSettings
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

        var adapter = CreateTestAdapter();
        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
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
        var adapter = CreateTestAdapter(requiresModel: true);
        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
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
    public async Task RunAsync_persist_model_saves_to_settings()
    {
        var adapter = CreateTestAdapter();
        var settings = new LauncherSettings
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

        var (orchestrator, tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            var exitCode = await orchestrator.RunAsync(
                "TestAgent",
                null, null, "gpt-4",
                ["model"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            var settingsDir = Path.Combine(tempDir, "settings");
            var settingsStore = new SettingsStore(settingsDir);
            var savedSettings = settingsStore.Load();
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
        var tempDir = CreateTempDir();
        try
        {
            var settingsDir = Path.Combine(tempDir, "settings");
            Directory.CreateDirectory(settingsDir);
            var backupsDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupsDir);

            var configFile = Path.Combine(tempDir, "config.json");
            await File.WriteAllTextAsync(configFile, "{}");

            var settingsStore = new SettingsStore(settingsDir);
            var backupService = new BackupService(backupsDir);
            var runner = new TestLaunchRunner();
            var adapter = new ConfigWritingAdapter(configFile);
            var orchestrator = new LaunchOrchestrator(settingsStore, backupService, runner, [adapter]);

            var exitCode = await orchestrator.RunAsync(
                "ConfigWriter",
                null, null, null,
                ["config"],
                tempDir);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            var content = await File.ReadAllTextAsync(configFile);
            await Assert.That(content).IsEqualTo("{\"modified\":true}");
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

    private class TestHarnessAdapter : IHarnessAdapter
    {
        private readonly bool _isAvailable;

        public HarnessMetadata Metadata { get; }
        public LaunchRequest? LastRequest { get; private set; }

        public TestHarnessAdapter(string name, bool isAvailable, bool requiresModel, string installHint)
        {
            _isAvailable = isAvailable;
            Metadata = new HarnessMetadata(
                name,
                HarnessKind.Cli,
                requiresModel,
                installHint,
                DetectionCommand: null,
                ConfigFileCandidates: []);
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_isAvailable);
        }

        public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var config = new ProcessConfiguration("test")
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
            HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "N/A",
            DetectionCommand: null,
            ConfigFileCandidates: []);

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
            var config = new ProcessConfiguration("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };

            var actions = new ConfigurationAction[]
            {
                new ConfigFileWriteAction(_configFile, "{\"modified\":true}")
            };

            return Task.FromResult(new LaunchPlan(config, actions));
        }
    }
}
