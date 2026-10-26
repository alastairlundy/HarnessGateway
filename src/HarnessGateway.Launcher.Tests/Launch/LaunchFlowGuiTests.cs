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
/// Pins the GUI branches of the flow itself: discovery-backed availability,
/// the <c>--confirm-running</c> misuse check, the live-instance confirmation
/// chain (proceed, decline, bypass), declared-surface gating for keys and
/// overrides, mode dispatch by persist, and the mode-honest completed
/// payload - with the restore guarantee holding across every new branch.
/// The GUI test adapter stands in for the not-yet-shipped Kind=Gui registry
/// entry; no test here starts a real process or touches a real harness.
/// </summary>
public class LaunchFlowGuiTests
{
    private const string ConfigOriginal = "# original gui config\n";
    private const string ModifiedConfig = "{\"modified\":true}";
    private const int HandedOffProcessId = 4242;

    private static readonly HashSet<ProtocolKind> AllProtocols =
    [
        ProtocolKind.OpenAiChat,
        ProtocolKind.OpenAiResponses,
        ProtocolKind.Anthropic,
    ];

    /// <summary>
    /// What a file-writing GUI test adapter declares: the surface its plan
    /// writes through, plus environment for the default key path. Without
    /// Files the flow would correctly filter the write away, and the test
    /// would assert against a file that was never touched.
    /// </summary>
    private static readonly HashSet<ConfigurationSurface> FileSurfaces =
    [
        ConfigurationSurface.Files,
        ConfigurationSurface.Environment,
    ];

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-gui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static LauncherSettings DefaultSettings() => new()
    {
        Backends =
        [
            new Backend
            {
                Name = "default",
                BaseUri = new Uri("https://api.default.com/v1"),
                Protocol = ProtocolKind.OpenAiChat,
            },
        ],
        DefaultBackend = "default",
    };

    private static LauncherSettings KeyedSettings(string reference) => new()
    {
        Backends =
        [
            new Backend
            {
                Name = "default",
                BaseUri = new Uri("https://api.default.com/v1"),
                Protocol = ProtocolKind.OpenAiChat,
                ApiKey = reference,
            },
        ],
        DefaultBackend = "default",
    };

    /// <summary>
    /// One flow with fakes on every new seam. Discovery defaults to a hit so
    /// a test that is not about availability is never mistaken for one, and
    /// <paramref name="tempDir"/> lets a test seed a config file into the
    /// same directory the flow's settings and backups live in, so one
    /// cleanup covers everything.
    /// </summary>
    private static (LaunchFlow flow, string tempDir, string backupsDir) CreateGuiFlow(
        IHarnessAdapter? adapter = null,
        LauncherSettings? settings = null,
        ILaunchRunner? runner = null,
        ISecureKeyStore? keyStore = null,
        FakeGuiInstallDiscovery? discovery = null,
        FakeRunningInstanceDetector? detector = null,
        FakeConfirmationPrompt? prompt = null,
        string? tempDir = null)
    {
        tempDir ??= CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        ISecureKeyStore effectiveKeyStore = keyStore ?? new FakeSecureKeyStore();
        SettingsStore settingsStore = new(settingsDir, effectiveKeyStore);
        settingsStore.Save(settings ?? DefaultSettings());

        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new RecordingLaunchRunner(),
            [adapter ?? new TestGuiHarnessAdapter()],
            effectiveKeyStore,
            discovery ?? new FakeGuiInstallDiscovery(found: true),
            detector ?? new FakeRunningInstanceDetector(),
            prompt ?? new FakeConfirmationPrompt());
        return (flow, tempDir, backupsDir);
    }

    /// <summary>
    /// A GUI-shaped config file the adapter writes, seeded with known bytes
    /// so restore can be asserted byte-for-byte.
    /// </summary>
    private static string SeedConfigFile(string tempDir)
    {
        string configPath = Path.Combine(tempDir, "gui-config.toml");
        File.WriteAllText(configPath, ConfigOriginal);
        return configPath;
    }

    // --- Availability: discovery, not the CLI probe ---

    [Test]
    public async Task ExecuteAsync_gui_availability_comes_from_discovery_not_the_adapter_probe()
    {
        TestGuiHarnessAdapter adapter = new();
        (LaunchFlow flow, string tempDir, string backupsDir) = CreateGuiFlow(adapter: adapter);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(new CompletedPayload.ExitStatus(7));
            // The adapter's own probe reports unavailable; reaching the
            // completed outcome proves discovery was the availability source.
            await Assert.That(adapter.AvailabilityProbed).IsFalse();
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_exhausted_gui_discovery_refuses_as_unavailable()
    {
        (LaunchFlow flow, string tempDir, _) = CreateGuiFlow(
            discovery: new FakeGuiInstallDiscovery(found: false));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.HarnessUnavailable);
            // The install hint is what an exhausted search tells the user.
            await Assert.That(refused.Message).Contains("Install the GUI app");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- The --confirm-running misuse check ---

    [Test]
    public async Task ExecuteAsync_confirm_running_on_a_cli_harness_refuses_as_misuse()
    {
        FakeConfirmationPrompt prompt = new();
        (LaunchFlow flow, string tempDir, _) = CreateGuiFlow(
            adapter: new CliTestAdapter(),
            prompt: prompt);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("TestAgent", null, null, null, null, [], null, ConfirmRunning: true));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.ConfirmUnsupportedForHarness);
            await Assert.That(refused.Message).Contains("--confirm-running");
            // Misuse never reaches the prompt the flag assumes exists.
            await Assert.That(prompt.PromptedMessages).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- The live-instance confirmation chain ---

    [Test]
    public async Task ExecuteAsync_live_instance_declined_stops_as_unconfirmed_before_any_write()
    {
        string tempDir = CreateTempDir();
        try
        {
            string configPath = SeedConfigFile(tempDir);
            TestGuiHarnessAdapter adapter = new(
                supportedSurfaces: FileSurfaces,
                actions: [new ConfigFileWriteAction(configPath, ModifiedConfig)]);
            FakeConfirmationPrompt prompt = new(confirmed: false);
            RecordingLaunchRunner runner = new();
            (LaunchFlow flow, _, string backupsDir) = CreateGuiFlow(
                adapter: adapter,
                runner: runner,
                detector: new FakeRunningInstanceDetector(found: true),
                prompt: prompt,
                tempDir: tempDir);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.RunningInstanceUnconfirmed);
            await Assert.That(prompt.PromptedMessages).Count().IsEqualTo(1);
            // Stopped before anything ran or was written.
            await Assert.That(runner.RunCallCount).IsEqualTo(0);
            await Assert.That(runner.HandoffCallCount).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(ConfigOriginal);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_live_instance_confirmed_prompt_proceeds()
    {
        FakeConfirmationPrompt prompt = new(confirmed: true);
        FakeRunningInstanceDetector detector = new(found: true);
        (LaunchFlow flow, string tempDir, _) = CreateGuiFlow(
            detector: detector,
            prompt: prompt);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(new CompletedPayload.ExitStatus(7));
            await Assert.That(prompt.PromptedMessages).Count().IsEqualTo(1);
            // The detector was asked with the harness's declared names.
            await Assert.That(detector.LastRequestedNames).IsNotNull();
            await Assert.That(
                detector.LastRequestedNames!.Contains("gui-agent-process")).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_confirm_running_override_skips_the_prompt_in_an_interactive_context()
    {
        FakeConfirmationPrompt prompt = new(confirmed: false);
        (LaunchFlow flow, string tempDir, _) = CreateGuiFlow(
            detector: new FakeRunningInstanceDetector(found: true),
            prompt: prompt);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null, ConfirmRunning: true));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(new CompletedPayload.ExitStatus(7));
            // The prompt was capable of answering - it would have declined -
            // yet never saw the question: the override skips it everywhere.
            await Assert.That(prompt.PromptedMessages).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Declared-surface gating: keys and overrides ---

    [Test]
    public async Task ExecuteAsync_key_with_only_file_surfaces_refuses_as_gui_backend_unsupported()
    {
        FakeSecureKeyStore keyStore = new();
        keyStore.Seed("hgkey:default", "sk-keyed");
        TestGuiHarnessAdapter adapter = new(
            supportedSurfaces: new HashSet<ConfigurationSurface> { ConfigurationSurface.Files });
        (LaunchFlow flow, string tempDir, string backupsDir) = CreateGuiFlow(
            adapter: adapter,
            settings: KeyedSettings("hgkey:default"),
            keyStore: keyStore);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.GuiBackendUnsupported);
            // The message says why and how to proceed; it never leaks the key.
            await Assert.That(refused.Message).Contains("environment");
            await Assert.That(refused.Message.Contains("sk-keyed")).IsFalse();
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_model_override_with_no_declared_surface_refuses_as_gui_backend_unsupported()
    {
        TestGuiHarnessAdapter adapter = new(
            supportedSurfaces: new HashSet<ConfigurationSurface>());
        (LaunchFlow flow, string tempDir, string backupsDir) = CreateGuiFlow(adapter: adapter);
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, "gpt-5", [], null));

            LaunchRefused refused = (LaunchRefused)outcome;
            await Assert.That(refused.Kind).IsEqualTo(LaunchRefusalKind.GuiBackendUnsupported);
            await Assert.That(refused.Message).Contains("override");
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_declared_surfaces_filter_the_planned_actions()
    {
        string tempDir = CreateTempDir();
        try
        {
            string configPath = SeedConfigFile(tempDir);
            // The plan deliberately over-delivers: a file write and a flag
            // action, against a harness that declares only Files.
            TestGuiHarnessAdapter adapter = new(
                supportedSurfaces: new HashSet<ConfigurationSurface> { ConfigurationSurface.Files },
                actions:
                [
                    new ConfigFileWriteAction(configPath, ModifiedConfig),
                    new CliArgumentAction("--model", "gpt-5"),
                ]);
            RecordingLaunchRunner runner = new();
            (LaunchFlow flow, _, string backupsDir) = CreateGuiFlow(
                adapter: adapter,
                runner: runner,
                tempDir: tempDir);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(new CompletedPayload.ExitStatus(7));
            // The runner saw the plan after filtering: no flag action rode
            // an undeclared surface, while the declared file write ran and
            // was restored on exit.
            await Assert.That(runner.LastPlan).IsNotNull();
            await Assert.That(
                runner.LastPlan!.ConfigurationActions.OfType<CliArgumentAction>()).IsEmpty();
            await Assert.That(
                runner.LastPlan.ConfigurationActions.OfType<ConfigFileWriteAction>())
                .Count().IsEqualTo(1);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(ConfigOriginal);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Mode dispatch and the mode-honest payload ---

    [Test]
    public async Task ExecuteAsync_persist_config_hands_off_and_carries_the_process_id()
    {
        string tempDir = CreateTempDir();
        try
        {
            string configPath = SeedConfigFile(tempDir);
            TestGuiHarnessAdapter adapter = new(
                supportedSurfaces: FileSurfaces,
                actions: [new ConfigFileWriteAction(configPath, ModifiedConfig)]);
            RecordingLaunchRunner runner = new();
            (LaunchFlow flow, _, string backupsDir) = CreateGuiFlow(
                adapter: adapter,
                runner: runner,
                tempDir: tempDir);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, ["config"], null));

            // Fire-and-forget: the handoff happened, the blocking run never
            // did, and the payload is the detached process id.
            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(
                new CompletedPayload.HandedOff(HandedOffProcessId));
            await Assert.That(runner.HandoffCallCount).IsEqualTo(1);
            await Assert.That(runner.RunCallCount).IsEqualTo(0);
            // Persisting kept the write durable and consumed its marker.
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(ModifiedConfig);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_without_persist_blocks_and_restores_the_config()
    {
        string tempDir = CreateTempDir();
        try
        {
            string configPath = SeedConfigFile(tempDir);
            TestGuiHarnessAdapter adapter = new(
                supportedSurfaces: FileSurfaces,
                actions: [new ConfigFileWriteAction(configPath, ModifiedConfig)]);
            RecordingLaunchRunner runner = new();
            (LaunchFlow flow, _, string backupsDir) = CreateGuiFlow(
                adapter: adapter,
                runner: runner,
                tempDir: tempDir);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            // Blocking mode: the flow waited on the application, so the
            // payload is its exit status, and the finally restored.
            LaunchCompleted completed = (LaunchCompleted)outcome;
            await Assert.That(completed.Payload).IsEqualTo(new CompletedPayload.ExitStatus(7));
            await Assert.That(runner.RunCallCount).IsEqualTo(1);
            await Assert.That(runner.HandoffCallCount).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(ConfigOriginal);
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_runner_fault_on_the_blocking_gui_path_still_restores()
    {
        string tempDir = CreateTempDir();
        try
        {
            string configPath = SeedConfigFile(tempDir);
            TestGuiHarnessAdapter adapter = new(
                supportedSurfaces: FileSurfaces,
                actions: [new ConfigFileWriteAction(configPath, ModifiedConfig)]);
            RecordingLaunchRunner runner = new();
            runner.RunFault = new InvalidOperationException("gui runner exploded");
            (LaunchFlow flow, _, _) = CreateGuiFlow(
                adapter: adapter,
                runner: runner,
                tempDir: tempDir);

            LaunchOutcome outcome = await flow.ExecuteAsync(
                new LaunchRequest("gui-agent", null, null, null, null, [], null));

            // The restore guarantee is unchanged across the new branches:
            // the fault folds to the terminal failure, never leaks as an
            // exception, and the finally still restored.
            LaunchFailed failed = (LaunchFailed)outcome;
            await Assert.That(failed.Kind).IsEqualTo(LaunchFailureKind.Unexpected);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo(ConfigOriginal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Fakes for this suite ---

    /// <summary>
    /// Kind=Gui stand-in: its availability probe deliberately reports
    /// unavailable so a completed launch proves discovery was consulted
    /// instead, and its plan is whatever the test asked for.
    /// </summary>
    private sealed class TestGuiHarnessAdapter : IHarnessAdapter
    {
        private readonly ConfigurationAction[] _actions;

        public HarnessMetadata Metadata { get; }

        public bool AvailabilityProbed { get; private set; }

        public TestGuiHarnessAdapter(
            IReadOnlySet<ConfigurationSurface>? supportedSurfaces = null,
            IReadOnlyList<string>? instanceProcessNames = null,
            ConfigurationAction[]? actions = null)
        {
            _actions = actions ?? [];
            Metadata = new HarnessMetadata(
                "GUI Test Agent",
                "gui-agent",
                [],
                HarnessKind.Gui,
                RequiresModel: false,
                InstallHint: "Install the GUI app from the vendor site.",
                DetectionCommand: "N/A",
                [],
                AllProtocols,
                supportedSurfaces ?? new HashSet<ConfigurationSurface>
                {
                    ConfigurationSurface.Environment,
                },
                instanceProcessNames ?? ["gui-agent-process"]);
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityProbed = true;
            return Task.FromResult(false);
        }

        public Task<LaunchPlan> PlanLaunchAsync(
            ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new("gui-app")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true,
            };
            return Task.FromResult(new LaunchPlan(config, _actions));
        }
    }

    /// <summary>
    /// Kind=Cli stand-in for the misuse check: a plain available CLI harness
    /// that must never see <c>--confirm-running</c> accepted.
    /// </summary>
    private sealed class CliTestAdapter : IHarnessAdapter
    {
        public HarnessMetadata Metadata { get; } = new(
            "CLI Test Agent",
            "TestAgent",
            [],
            HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "N/A",
            DetectionCommand: "TestAgent --version",
            [],
            AllProtocols,
            new HashSet<ConfigurationSurface>(),
            []);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<LaunchPlan> PlanLaunchAsync(
            ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            ProcessConfiguration config = new("test")
            {
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true,
            };
            return Task.FromResult(new LaunchPlan(config, []));
        }
    }

    /// <summary>
    /// The runner fake this suite observes: a fixed blocking exit status, a
    /// fixed handoff pid, per-mode call counts, the plan it was handed, and
    /// an optional fault for the restore path.
    /// </summary>
    private sealed class RecordingLaunchRunner : ILaunchRunner
    {
        public int RunCallCount { get; private set; }

        public int HandoffCallCount { get; private set; }

        public LaunchPlan? LastPlan { get; private set; }

        public Exception? RunFault { get; set; }

        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            RunCallCount++;
            LastPlan = plan;
            if (RunFault is not null)
            {
                throw RunFault;
            }

            return Task.FromResult(7);
        }

        public int Handoff(LaunchPlan plan)
        {
            HandoffCallCount++;
            LastPlan = plan;
            return HandedOffProcessId;
        }
    }
}
