using System.CommandLine;
using System.Net;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Update;
using HarnessGateway.Launcher.Models;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

/// <summary>
/// Parse-and-invoke tests for `update`: the command
/// is driven through the real System.CommandLine pipeline over a stub HTTP
/// layer and a fake clock, so these tests pin exit codes and the
/// cooldown's remaining time - message wording is user experience, not
/// contract, and is deliberately not asserted beyond naming the subcommand.
/// </summary>
public class UpdateCommandTests
{
    private static readonly DateTimeOffset Baseline = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-updatecommand-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (RootCommand Root, StubHttpMessageHandler Handler, FakeTimeProvider Clock, string Dir) CreateRoot(
        string responseBody = """{"tag_name":"v0.1.0"}""",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Exception? failure = null)
    {
        string dir = CreateTempDir();
        StubHttpMessageHandler handler = new(responseBody, statusCode, failure);
        FakeTimeProvider clock = new(Baseline);
        UpdateChecker checker = new(
            new HttpClient(handler),
            clock,
            new UpdateCooldownStore(dir),
            currentVersion: "0.1.0");

        RootCommand root = new RootCommand { new UpdateCommand(checker) };
        return (root, handler, clock, dir);
    }

    [Test]
    public async Task Bare_update_refuses_naming_the_check_subcommand_and_runs_nothing()
    {
        (RootCommand root, StubHttpMessageHandler handler, _, string dir) = CreateRoot();
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(
                () => root.Parse("update").InvokeAsync());

            // Non-zero refusal; only the subcommand's name is
            // contractual - the surrounding sentence is UX.
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("update check");

            // Defaults to no behavior: no network call, no cooldown stamp.
            await Assert.That(handler.CallCount).IsEqualTo(0);
            await Assert.That(File.Exists(Path.Combine(dir, UpdateCooldownStore.FileName))).IsFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Update_check_up_to_date_exits_zero()
    {
        (RootCommand root, StubHttpMessageHandler handler, _, string dir) = CreateRoot();
        try
        {
            (int exitCode, _) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Update_check_with_a_newer_release_exits_update_available()
    {
        (RootCommand root, _, _, string dir) =
            CreateRoot(responseBody: """{"tag_name":"v0.2.0"}""");
        try
        {
            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UpdateAvailable);
            // The published tag reaches the user's terminal, not just the
            // exit code - data, not wording.
            await Assert.That(output).Contains("v0.2.0");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Update_check_network_failure_exits_check_failed()
    {
        (RootCommand root, _, _, string dir) = CreateRoot(
            failure: new HttpRequestException("connection refused"));
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.CheckFailed);
            await Assert.That(errorOutput).IsNotEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Update_check_unparseable_response_exits_check_failed()
    {
        (RootCommand root, _, _, string dir) = CreateRoot(responseBody: "this is not json");
        try
        {
            (int exitCode, _) = await ConsoleErrorCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Update_check_inside_the_cooldown_exits_zero_and_prints_the_remaining_time()
    {
        (RootCommand root, StubHttpMessageHandler handler, FakeTimeProvider clock, string dir) = CreateRoot();
        try
        {
            (int firstExit, _) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());
            await Assert.That(firstExit).IsEqualTo((int)LaunchExitCode.Ok);

            clock.Advance(TimeSpan.FromSeconds(1));

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());

            // No second network call, the remaining window on
            // stdout, and exit 0 - the notice is not a contract row.
            await Assert.That(handler.CallCount).IsEqualTo(1);
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
            await Assert.That(output).Contains("9m 59s");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Force_flag_bypasses_the_cooldown_window()
    {
        (RootCommand root, StubHttpMessageHandler handler, FakeTimeProvider clock, string dir) = CreateRoot();
        try
        {
            (int firstExit, _) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check").InvokeAsync());
            await Assert.That(firstExit).IsEqualTo((int)LaunchExitCode.Ok);

            clock.Advance(TimeSpan.FromSeconds(1));

            (int exitCode, _) = await ConsoleOutputCapture.RunAsync(
                () => root.Parse("update check --force").InvokeAsync());

            // The discriminating evidence: a forced run reaches the network
            // even one second into the window.
            await Assert.That(handler.CallCount).IsEqualTo(2);
            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.Ok);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Production_root_registers_update_with_the_check_subcommand()
    {
        (RootCommand root, string tempDir) = CreateProductionRoot();
        try
        {
            Command? update = root.Subcommands.FirstOrDefault(c => c.Name == "update");
            await Assert.That(update).IsNotNull();

            // The surface itself: update on top, check beneath it,
            // and the force flag on check - not on the bare command.
            await Assert.That(update.Subcommands.Select(c => c.Name)).Contains("check");
            await Assert.That(update.Options).IsEmpty();
            await Assert.That(update.Subcommands.Single(c => c.Name == "check").Options)
                .Contains(UpdateCommand.ForceOption);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Bare_update_on_the_production_root_refuses()
    {
        (RootCommand root, string tempDir) = CreateProductionRoot();
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(
                () => root.Parse("update").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("update check");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// The factory-built root the entry point uses. The runner throws on
    /// purpose: nothing in the update surface may ever reach the launch flow.
    /// </summary>
    private static (RootCommand Root, string TempDir) CreateProductionRoot()
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore keyStore = new();
        SettingsStore settingsStore = new(settingsDir, keyStore);
        LaunchOrchestrator orchestrator = new(new LaunchFlow(
            settingsStore,
            new BackupService(backupsDir),
            new RefuseToLaunchRunner(),
            [],
            keyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
new FakeTerminalProbe(),
            QualificationDeps.Cache(tempDir),
            QualificationDeps.Qualifier,
            new ScriptedModelChoicePrompt(), new ApiKeyScrubList()));
        RootCommand root = RootCommandFactory.Create(
            orchestrator,
            _ => Task.FromResult(0),
            [],
            settingsStore,
            keyStore);
        return (root, tempDir);
    }

    private sealed class RefuseToLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The update surface must never reach the launch runner.");
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
            throw new InvalidOperationException("The update surface must never reach the launch runner.");
        }
    }
}
