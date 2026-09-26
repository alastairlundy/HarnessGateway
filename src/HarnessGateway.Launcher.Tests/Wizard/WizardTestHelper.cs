using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Wizard;

public static class WizardTestHelper
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-wizard-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsStore CreateSettingsStore(string tempDir, LauncherSettings? settings = null)
    {
        var settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        var store = new SettingsStore(settingsDir);
        if (settings is not null)
        {
            store.Save(settings);
        }
        return store;
    }

    public static (string tempDir, SettingsStore store) CreateTestEnvironment(LauncherSettings? settings = null)
    {
        var tempDir = CreateTempDir();
        var store = CreateSettingsStore(tempDir, settings);
        return (tempDir, store);
    }

    public static LauncherSettings CreateDefaultSettings(
        string? endpoint = null,
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? apiKey = null,
        string? defaultModel = null)
    {
        return new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri(endpoint ?? "https://api.example.com/v1"),
                    Protocol = protocol,
                    ApiKey = apiKey,
                    DefaultModel = defaultModel
                }
            ],
            DefaultBackend = "default"
        };
    }

    public static Core.Adapters.IHarnessAdapter CreateTestAdapter(
        string name = "TestAgent",
        bool isAvailable = true,
        bool requiresModel = false)
    {
        return new TestHarnessAdapterForWizard(name, isAvailable, requiresModel);
    }

    private sealed class TestHarnessAdapterForWizard : Core.Adapters.IHarnessAdapter
    {
        private readonly bool _isAvailable;
        public Core.Adapters.HarnessMetadata Metadata { get; }

        public TestHarnessAdapterForWizard(string name, bool isAvailable, bool requiresModel)
        {
            _isAvailable = isAvailable;
            Metadata = new Core.Adapters.HarnessMetadata(
                name,
                HarnessKind.Cli,
                requiresModel,
                $"Install {name}",
                DetectionCommand: null,
                [],
                new HashSet<Core.Backends.ProtocolKind>
                {
                    Core.Backends.ProtocolKind.OpenAiChat,
                    Core.Backends.ProtocolKind.OpenAiResponses,
                    Core.Backends.ProtocolKind.Anthropic
                });
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_isAvailable);
        }

        public Task<Core.Launch.LaunchPlan> PlanLaunchAsync(Core.Launch.LaunchRequest request, CancellationToken cancellationToken = default)
        {
            var config = new CliInvoke.Core.ProcessConfiguration("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new Core.Launch.LaunchPlan(config, []));
        }
    }
}
