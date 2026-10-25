using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
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
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-wizard-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsStore CreateSettingsStore(string tempDir, LauncherSettings? settings = null)
    {
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        SettingsStore store = new SettingsStore(settingsDir);
        if (settings is not null)
        {
            store.Save(settings);
        }
        return store;
    }

    public static (string tempDir, SettingsStore store, FakeSecureKeyStore keyStore) CreateTestEnvironment(
        LauncherSettings? settings = null)
    {
        string tempDir = CreateTempDir();
        SettingsStore store = CreateSettingsStore(tempDir, settings);
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

    public static IHarnessAdapter CreateTestAdapter(
        string name = "TestAgent",
        bool isAvailable = true,
        bool requiresModel = false,
        IReadOnlySet<ProtocolKind>? supportedProtocols = null)
    {
        return new TestHarnessAdapterForWizard(name, isAvailable, requiresModel, supportedProtocols);
    }

    private sealed class TestHarnessAdapterForWizard : IHarnessAdapter
    {
        private readonly bool _isAvailable;
        public HarnessMetadata Metadata { get; }

        public TestHarnessAdapterForWizard(
            string name,
            bool isAvailable,
            bool requiresModel,
            IReadOnlySet<ProtocolKind>? supportedProtocols,
            string? commandName = null)
        {
            _isAvailable = isAvailable;
            Metadata = new HarnessMetadata(
                name,
                // Most fakes use one string for both the wizard label and the
                // launch token, which is realistic. Pass commandName explicitly
                // only to model a harness whose two differ.
                commandName ?? name,
                [],
                HarnessKind.Cli,
                requiresModel,
                $"Install {name}",
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

        public Task<Core.Launch.LaunchPlan> PlanLaunchAsync(Core.Launch.ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new CliInvoke.Core.ProcessConfiguration("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new Core.Launch.LaunchPlan(config, []));
        }
    }
}
