using System.CommandLine;
using CliInvoke.Core;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Models;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

/// <summary>
/// Regression tests for the launch wiring: `harnessgateway launch [harness]`
/// must reach the orchestrator through the real parse-and-invoke pipeline.
/// System.CommandLine's no-action fallback silently succeeds with exit 0, and
/// the orchestrator-level tests call RunAsync directly instead of parsing, so
/// neither can catch missing wiring on its own. </summary>
public class LaunchCommandInvocationTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-launchinvocation-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (RootCommand Root, FakeHarnessAdapter Adapter, string TempDir) CreateRoot(
        FakeHarnessAdapter adapter,
        string? settingsJson = null,
        int runnerExitCode = 0,
        Func<CancellationToken, Task<int>>? wizardAction = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        SettingsStore settingsStore = new SettingsStore(settingsDir, new FakeSecureKeyStore());
        if (settingsJson is not null)
        {
            File.WriteAllText(settingsStore.SettingsFilePath, settingsJson);
        }

        BackupService backupService = new BackupService(backupsDir);
        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            new LaunchFlow(
                settingsStore,
                backupService,
                new StubLaunchRunner(runnerExitCode),
                [adapter],
                new FakeSecureKeyStore(),
                new FakeGuiInstallDiscovery(),
                new FakeRunningInstanceDetector(),
                new FakeConfirmationPrompt(),
                new ProtocolEmissionPlanner(),
new FakeTerminalProbe(),
                QualificationDeps.Cache(tempDir),
                QualificationDeps.Qualifier,
                new ScriptedModelChoicePrompt(), new ApiKeyScrubList()));
        LaunchCommand launchCommand = new LaunchCommand(orchestrator, wizardAction);
        return (new RootCommand { launchCommand }, adapter, tempDir);
    }

    [Test]
    public async Task Invoke_bare_launch_runs_the_wizard_not_the_orchestrator()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        bool wizardCalled = false;
        (RootCommand root, _, string tempDir) = CreateRoot(
            adapter,
            wizardAction: _ =>
            {
                wizardCalled = true;
                return Task.FromResult(17);
            });
        try
        {
            int exitCode = await root.Parse("launch").InvokeAsync();

            await Assert.That(wizardCalled).IsTrue();
            await Assert.That(exitCode).IsEqualTo(17);
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_bare_launch_without_a_wizard_fails_loudly_not_silently()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("A harness identifier is required");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_launch_subcommand_runs_the_orchestrator()
    {
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            // Errors surface through exit codes here; nothing is asserted on
            // stderr because Console.Error is a process-global redirect target
            // that parallel tests write into while captures are held.
            int exitCode = await root
                .Parse("launch TestAgent --endpoint https://api.example.com/v1 --protocol openai-chat")
                .InvokeAsync();

            await Assert.That(exitCode).IsEqualTo(0);
            // The orchestrator actually ran: the parsed flags reached the adapter
            // as a launch request. Any of these failing would re-expose the
            // silent no-op this suite guards against.
            await Assert.That(adapter.LastRequest).IsNotNull();
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
    public async Task Invoke_launch_with_confirm_running_reaches_the_flow()
    {
        // The flag is parsed CLI-side and carried through the orchestrator
        // into the LaunchRequest: a parse-only flag would leave this launch
        // identical to one without it. The flow-side consumption on a
        // Kind=Cli harness (the misuse refusal) is pinned here through its
        // exit code; the GUI-side skip and prompt chain are pinned in
        // LaunchFlowGuiTests.
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch TestAgent --confirm-running " +
                           "--endpoint https://api.example.com/v1 --protocol openai-chat")
                    .InvokeAsync());

            // The flag is consumed flow-side now: on a Kind=Cli harness it
            // surfaces as the misuse refusal, and reaching that refusal is
            // itself the proof the parsed flag made it into the LaunchRequest -
            // without it this launch would have exited 0 and run the adapter.
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("--confirm-running");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_unknown_agent_surfaces_exit_3_instead_of_silent_success()
    {
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch NoSuchAgent").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
            await Assert.That(errorOutput).Contains("Unknown harness: NoSuchAgent");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_unknown_provider_name_surfaces_exit_8_through_the_cli()
    {
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter, settingsJson: """
            {
              "backends": [
                {
                  "name": "primary",
                  "baseUri": "https://primary.example/v1",
                  "protocol": 0
                }
              ],
              "defaultBackend": "primary",
              "schemaVersion": 2
            }
            """);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch TestAgent --provider nope").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownProviderName);
            await Assert.That(errorOutput).Contains("unknown provider name 'nope'");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_launch_with_print_reaches_the_flow_as_print()
    {
        // Same proof shape as --confirm-running: FakeHarnessAdapter has no
        // one-shot mode, so a parsed -p surfaces as the flow's capability
        // refusal - reaching that refusal is itself the proof the flag made it
        // into the LaunchRequest. Without it this launch would have exited 0
        // and run the adapter interactively.
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch TestAgent -p \"hello there\" " +
                           "--endpoint https://api.example.com/v1 --protocol openai-chat")
                    .InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("-p/--print");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_launch_with_print_long_spelling_reaches_the_flow_as_print()
    {
        FakeHarnessAdapter adapter = new("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch TestAgent --print \"hello there\" " +
                           "--endpoint https://api.example.com/v1 --protocol openai-chat")
                    .InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("-p/--print");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class FakeHarnessAdapter : IHarnessAdapter
    {
        public HarnessMetadata Metadata { get; }
        public ResolvedLaunch? LastRequest { get; private set; }

        public FakeHarnessAdapter(string name)
        {
            Metadata = new HarnessMetadata(
                name,
                name,
                [],
                HarnessKind.Cli,
                RequiresModel: false,
                InstallHint: "<install hint>",
                $"{name} --version",
                [],
                new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses,
                    ProtocolKind.Anthropic
                },
                new HashSet<ConfigurationSurface>(),
                []);
        }

        public Task<AvailabilityResult> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AvailabilityResult.Found);
        }

        public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ProcessConfiguration process = new ProcessConfiguration("test")
            {
                Arguments = "run",
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new LaunchPlan(process, []));
        }
    }

    private sealed class StubLaunchRunner(int exitCode) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(exitCode);
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
            return exitCode;
        }
    }
}
