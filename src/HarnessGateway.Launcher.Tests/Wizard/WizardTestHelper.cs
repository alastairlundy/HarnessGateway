using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Wizard;

public static class WizardTestHelper
{
    private static readonly IReadOnlySet<ProtocolKind> AllProtocols =
        new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic
        };

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

    public static (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) CreateTestEnvironment(
        LauncherSettings? settings = null)
    {
        var tempDir = CreateTempDir();
        var store = CreateSettingsStore(tempDir, settings);
        return (tempDir, store, new FakeSecureKeyStore());
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
        bool requiresModel = false,
        IReadOnlySet<ProtocolKind>? supportedProtocols = null)
    {
        return new TestHarnessAdapterForWizard(name, isAvailable, requiresModel, supportedProtocols);
    }

    private sealed class TestHarnessAdapterForWizard : Core.Adapters.IHarnessAdapter
    {
        private readonly bool _isAvailable;
        public Core.Adapters.HarnessMetadata Metadata { get; }

        public TestHarnessAdapterForWizard(
            string name,
            bool isAvailable,
            bool requiresModel,
            IReadOnlySet<ProtocolKind>? supportedProtocols)
        {
            _isAvailable = isAvailable;
            Metadata = new Core.Adapters.HarnessMetadata(
                name,
                HarnessKind.Cli,
                requiresModel,
                $"Install {name}",
                DetectionCommand: null,
                [],
                supportedProtocols ?? AllProtocols);
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
