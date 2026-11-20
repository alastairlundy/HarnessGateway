using CliInvoke.Core;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Models;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

/// <summary>
/// Thin mapping and wiring suite for the CLI-side adapter:
/// every <see cref="LaunchRefusalKind"/> and <see cref="LaunchFailureKind"/>
/// maps to its contract exit code byte-for-byte, the "Launch failed:" prefix
/// is single-honest-source (Unexpected only), and <c>RunAsync</c> wires parsed
/// flags into a <see cref="LaunchRequest"/> the flow executes. End-to-end
/// launch behaviour is pinned in LaunchFlowTests - no duplication here.
/// </summary>
public class LaunchOrchestratorTests
{
    // --- Outcome-to-exit-code mapping: exhaustive per kind ---

    [Test]
    [Arguments(LaunchRefusalKind.NoProvider, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.UnknownHarness, (int)LaunchExitCode.UnknownAgent)]
    [Arguments(LaunchRefusalKind.HarnessUnavailable, (int)LaunchExitCode.HarnessUnavailable)]
    [Arguments(LaunchRefusalKind.ProtocolNotSupported, (int)LaunchExitCode.UnknownAgent)]
    [Arguments(LaunchRefusalKind.ProviderBlockConflict, (int)LaunchExitCode.ConfigConflict)]
    [Arguments(LaunchRefusalKind.UnknownProviderName, (int)LaunchExitCode.UnknownProviderName)]
    [Arguments(LaunchRefusalKind.DanglingDefaultProvider, (int)LaunchExitCode.UnknownProviderName)]
    [Arguments(LaunchRefusalKind.AbsentDefaultProvider, (int)LaunchExitCode.UnknownProviderName)]
    [Arguments(LaunchRefusalKind.ProtocolWithoutEndpoint, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.EndpointWithoutProtocol, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.InvalidEndpoint, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.InvalidProtocol, (int)LaunchExitCode.UsageError)]
    // The unlisted-model refusal reuses exit 3: no new exit code, the
    // README table keeps its size.
    [Arguments(LaunchRefusalKind.UnlistedModel, (int)LaunchExitCode.UnknownAgent)]
    // The three GUI kinds are producer additions to existing families:
    // unconfirmed instance and incapable surface both ride the
    // unavailable-harness number, flag misuse rides the usage number.
    [Arguments(LaunchRefusalKind.RunningInstanceUnconfirmed, (int)LaunchExitCode.HarnessUnavailable)]
    [Arguments(LaunchRefusalKind.GuiProviderUnsupported, (int)LaunchExitCode.HarnessUnavailable)]
    [Arguments(LaunchRefusalKind.ConfirmUnsupportedForHarness, (int)LaunchExitCode.UsageError)]
    // The two -p/--print misuses ride the usage number like every other
    // bad-flag stop; an interactive launch with no terminal is the one new
    // number (13) - correctly requested, wrong context.
    [Arguments(LaunchRefusalKind.PrintUnsupportedForHarness, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.PrintPromptMissing, (int)LaunchExitCode.UsageError)]
    [Arguments(LaunchRefusalKind.TerminalRequired, (int)LaunchExitCode.TerminalRequired)]
    public async Task MapOutcomeAsync_refusal_kind_maps_to_its_exit_code(
        LaunchRefusalKind kind, int expectedExitCode)
    {
        // A synthetic message proves the mapping prints the outcome's message
        // as-is: never rebuilt, never decorated, and never given the
        // unexpected-fault prefix.
        const string message = "the flow composed this refusal message";
        (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
            LaunchOrchestrator.MapOutcomeAsync(new LaunchRefused(kind, message)));

        await Assert.That(exitCode).IsEqualTo(expectedExitCode);
        await Assert.That(errorOutput).IsEqualTo(message + Environment.NewLine);
    }

    [Test]
    [Arguments(LaunchFailureKind.CredentialStoreAbsentKey, (int)LaunchExitCode.ConfigRestoreFailure)]
    [Arguments(LaunchFailureKind.CredentialStoreError, (int)LaunchExitCode.ConfigRestoreFailure)]
    [Arguments(LaunchFailureKind.UnsupportedSettingsSchema, (int)LaunchExitCode.UnsupportedSettingsSchema)]
    public async Task MapOutcomeAsync_non_unexpected_failure_maps_without_the_prefix(
        LaunchFailureKind kind, int expectedExitCode)
    {
        const string message = "the flow composed this failure message";
        (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
            LaunchOrchestrator.MapOutcomeAsync(new LaunchFailed(kind, message)));

        await Assert.That(exitCode).IsEqualTo(expectedExitCode);
        // Single-honest-source prefix rule: only Unexpected may wear
        // "Launch failed:"; every other failure prints the outcome's message
        // exactly as composed.
        await Assert.That(errorOutput).IsEqualTo(message + Environment.NewLine);
    }

    [Test]
    public async Task MapOutcomeAsync_unexpected_failure_applies_the_launch_failed_prefix()
    {
        const string message = "the runner exploded";
        (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
            LaunchOrchestrator.MapOutcomeAsync(
                new LaunchFailed(LaunchFailureKind.Unexpected, message)));

        await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
        await Assert.That(errorOutput).IsEqualTo($"Launch failed: {message}{Environment.NewLine}");
    }

    [Test]
    public async Task MapOutcomeAsync_completed_carries_the_harness_exit_status()
    {
        (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
            LaunchOrchestrator.MapOutcomeAsync(
                new LaunchCompleted(new CompletedPayload.ExitStatus(42))));

        await Assert.That(exitCode).IsEqualTo(42);
        // A completed launch prints nothing on stderr.
        await Assert.That(errorOutput).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task MapOutcomeAsync_handed_off_completed_maps_to_exit_0()
    {
        // A detached start reports the handoff it already succeeded at: exit
        // 0, with no stderr line - there was no exit status to wait for.
        (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
            LaunchOrchestrator.MapOutcomeAsync(
                new LaunchCompleted(new CompletedPayload.HandedOff(4242))));

        await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
        await Assert.That(errorOutput).IsEqualTo(string.Empty);
    }

    // --- Wiring: RunAsync builds the request, the flow executes it ---

    [Test]
    public async Task RunAsync_unknown_agent_returns_exit_code_3()
    {
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator();
        try
        {
            // Wrapped in the capture gate: the "Unknown harness" stderr write
            // must not bleed into whoever holds Console.Error redirected.
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                orchestrator.RunAsync("NonExistentAgent", null, null, null, null, []));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
            // The stderr line is the outcome's message, printed as-is.
            await Assert.That(errorOutput).Contains("Unknown harness: NonExistentAgent.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_available_agent_runs_successfully()
    {
        TestHarnessAdapter adapter = new TestHarnessAdapter("TestAgent");
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent", null, null, null, null, []);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            // Parsed flags reached the adapter as a resolved launch: the
            // orchestrator is a request-building adapter over the flow, and
            // the default-backend pointer is what the request resolved to -
            // the stored entry riding the payload's provider case.
            await Assert.That(adapter.LastRequest).IsNotNull();
            LaunchResolution.ProviderCase providerCase =
                (LaunchResolution.ProviderCase)adapter.LastRequest.Launch;
            await Assert.That(providerCase.Provider.Name).IsEqualTo("default");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_carries_the_harness_exit_status_through()
    {
        TestHarnessAdapter adapter = new TestHarnessAdapter("TestAgent");
        // A non-zero harness exit proves RunAsync returns the completed
        // outcome's status, not a mapped constant.
        ILaunchRunner runner = new CallbackLaunchRunner(_ => Task.FromResult(3));
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.default.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = "default"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings, runner: runner);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent", null, null, null, null, []);

            await Assert.That(exitCode).IsEqualTo(3);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RunAsync_persist_values_reach_the_flow_through_the_request()
    {
        TestHarnessAdapter adapter = new TestHarnessAdapter("TestAgent");
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "test-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = "test-backend"
        };
        (LaunchOrchestrator orchestrator, string tempDir) = CreateOrchestrator(
            adapter: adapter, settings: settings);
        try
        {
            int exitCode = await orchestrator.RunAsync(
                "TestAgent", null, null, null, "gpt-4", ["model"]);

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);

            // The parsed --persist tokens rode the request: the flow's model
            // persist ran after run and restore.
            string settingsDir = Path.Combine(tempDir, "settings");
            LauncherSettings savedSettings = (await new SettingsStore(settingsDir).LoadAsync());
            await Assert.That(savedSettings.Providers[0].DefaultModel).IsEqualTo("gpt-4");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Construction helpers ---

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (LaunchOrchestrator orchestrator, string tempDir) CreateOrchestrator(
        IHarnessAdapter? adapter = null,
        LauncherSettings? settings = null,
        ILaunchRunner? runner = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
        SettingsStore settingsStore = new SettingsStore(settingsDir, keyStore);
        if (settings is not null)
        {
            settingsStore.Save(settings);
        }

        // Same collaborators the production wiring shares; the orchestrator
        // is now a thin adapter over the concrete flow.
        LaunchFlow flow = new LaunchFlow(
            settingsStore,
            new BackupService(backupsDir),
            runner ?? new TestLaunchRunner(),
            adapter is not null ? [adapter] : [],
            keyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            new FakeTerminalProbe());
        return (new LaunchOrchestrator(flow), tempDir);
    }

    // --- Minimal fakes: wiring tests need only planning and running ---

    private sealed class TestLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
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
            return 0;
        }
    }

    private sealed class CallbackLaunchRunner(Func<LaunchPlan, Task<int>> onRun) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return onRun(plan);
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
            return onRun(plan).GetAwaiter().GetResult();
        }
    }

    private sealed class TestHarnessAdapter : IHarnessAdapter
    {
        public HarnessMetadata Metadata { get; }

        public ResolvedLaunch? LastRequest { get; private set; }

        public TestHarnessAdapter(string name)
        {
            Metadata = new HarnessMetadata(
                name,
                name,
                [],
                HarnessKind.Cli,
                RequiresModel: false,
                InstallHint: "Install via: test-install",
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

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
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
}
