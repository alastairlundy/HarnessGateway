using System.Diagnostics;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Execution;

/// <summary>
/// The suite's single real-process test, for the detached start only: a real
/// child goes through <see cref="ILaunchRunner.Handoff"/>, and the contract
/// under test is that the call returns immediately with that child's live
/// process id instead of blocking on it the way <c>RunAsync</c> does. Every
/// other handoff behavior stays pinned by the flow-level fakes; nothing here
/// starts a real harness, and the child is always killed in cleanup.
/// </summary>
public class LaunchRunnerHandoffTests
{
    // The child outlives any accidental blocking call by a wide margin, so
    // "returned early" is provable: a blocking handoff would take at least
    // this long, well past the bound asserted below.
    private const int ChildLifetimeSeconds = 60;

    private static readonly TimeSpan HandoffMustReturnWithin = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Handoff_returns_immediately_with_a_live_process_id()
    {
        LaunchRunner runner = new();
        ProcessConfiguration config = SleepingShellCommand();
        LaunchPlan plan = new LaunchPlan(config, []);

        Stopwatch stopwatch = Stopwatch.StartNew();
        int processId = runner.Handoff(plan);
        stopwatch.Stop();

        using Process child = Process.GetProcessById(processId);
        try
        {
            await Assert.That(stopwatch.Elapsed).IsLessThan(HandoffMustReturnWithin);
            await Assert.That(processId).IsGreaterThan(0);
            // The pid names a process that is still running: handoff reported
            // the child it started, not an exited one and not a placeholder.
            await Assert.That(child.HasExited).IsFalse();
        }
        finally
        {
            // Cleanup even when an assertion above failed: kill the whole
            // tree, since the shell may have children of its own.
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }

    // CI runs on Linux, where "cmd" does not exist; /bin/sh is rooted so
    // CliInvoke's PATH/PATHEXT probing cannot mis-resolve it on Unix either.
    // Windows has no sleep(1), so ping ticks out the same duration instead.
    private static ProcessConfiguration SleepingShellCommand()
    {
        return OperatingSystem.IsWindows()
            ? new ProcessConfiguration("cmd", $"/c ping -n {ChildLifetimeSeconds + 1} 127.0.0.1", outputRedirection: false)
            : new ProcessConfiguration("/bin/sh", ["-c", $"sleep {ChildLifetimeSeconds}"], outputRedirection: false);
    }
}
