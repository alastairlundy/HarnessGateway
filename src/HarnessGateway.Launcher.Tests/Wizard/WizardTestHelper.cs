using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
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
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri(endpoint ?? "https://api.example.com/v1"),
                    DefaultProtocol = protocol,
                    ApiKey = apiKey,
                    DefaultModel = defaultModel
                }
            ],
            DefaultProvider = "default"
        };
    }

    public static IHarnessAdapter CreateTestAdapter(
        string name = "TestAgent",
        AvailabilityResult? availability = null,
        bool requiresModel = false,
        IReadOnlySet<ProtocolKind>? supportedProtocols = null)
    {
        return new TestHarnessAdapterForWizard(
            name, availability ?? AvailabilityResult.Found, requiresModel, supportedProtocols);
    }

    private sealed class TestHarnessAdapterForWizard : IHarnessAdapter
    {
        private readonly AvailabilityResult _availability;
        public HarnessMetadata Metadata { get; }

        public TestHarnessAdapterForWizard(
            string name,
            AvailabilityResult availability,
            bool requiresModel,
            IReadOnlySet<ProtocolKind>? supportedProtocols,
            string? commandName = null)
        {
            _availability = availability;
            Metadata = new HarnessMetadata(
                name,
                // Most fakes use one string for both the wizard label and the
                // launch token, which is realistic. Pass commandName explicitly
                // only to model a harness whose two differ.
                commandName ?? name,
                [],
                HarnessKind.Cli,
                requiresModel,
                $"<install hint for {name}>",
                $"{name} --version",
                [],
                supportedProtocols ?? AllProtocols,
                new HashSet<ConfigurationSurface>(),
                []);
        }

        public Task<AvailabilityResult> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_availability);
        }

        public Task<Core.Launch.LaunchPlan> PlanLaunchAsync(Core.Launch.ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new ProcessConfiguration("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new Core.Launch.LaunchPlan(config, []));
        }
    }
}
