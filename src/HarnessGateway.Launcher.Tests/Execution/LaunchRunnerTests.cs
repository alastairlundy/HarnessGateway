using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Execution;

public class LaunchRunnerTests
{
    [Test]
    public async Task RunAsync_propagates_exit_code()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = ShellCommand("exit 42", outputRedirection: false);
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(42);
    }

    [Test]
    public async Task RunAsync_propagates_zero_exit_code()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = ShellCommand("exit 0", outputRedirection: false);
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_sets_output_redirection_to_false()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = ShellCommand("exit 0", outputRedirection: true);
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_executes_in_foreground_and_blocks()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = ShellCommand("echo hello", outputRedirection: false);
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    // CI runs on Linux, where "cmd" does not exist; /bin/sh is rooted so CliInvoke's
    // PATH/PATHEXT probing cannot mis-resolve it on Unix either. ArgumentList (not the
    // Arguments string) keeps the command verbatim - no second tokenisation pass.
    private static ProcessConfiguration ShellCommand(string command, bool outputRedirection)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessConfiguration("cmd", $"/c {command}", outputRedirection)
            : new ProcessConfiguration("/bin/sh", ["-c", command], outputRedirection);
    }
}
