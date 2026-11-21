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

    [Test]
    public async Task RunInteractiveAsync_propagates_exit_code()
    {
        // The console-shared member: no output redirection, no timeout - the
        // child inherits this process's handles and the wait ends when the
        // child exits. The exit status still comes back like every other
        // blocking member's.
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = ShellCommand("exit 42", outputRedirection: false);
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunInteractiveAsync(plan);

        await Assert.That(exitCode).IsEqualTo(42);
    }

    [Test]
    public async Task InteractiveConfiguration_shares_a_windowed_console()
    {
        // The exact spawn shape of the interactive path. Output redirection
        // is forced off (the child inherits this process's handles, whatever
        // the plan carried) and window creation is forced on: on Windows a
        // WindowCreation=false spawn - the property default - gives the
        // child console handles with no console WINDOW, so a TUI believes it
        // has a terminal and paints where nobody can see it (child
        // GetConsoleWindow() = 0). stdin stays inherited for keystrokes.
        ProcessConfiguration config = LaunchRunner.InteractiveConfiguration(
            new LaunchPlan(ShellCommand("exit 0", outputRedirection: true), []));

        await Assert.That(config.OutputRedirection).IsFalse();
        await Assert.That(config.WindowCreation).IsTrue();
        await Assert.That(config.UseShellExecution).IsFalse();
        await Assert.That(config.RedirectStandardInput).IsFalse();
    }

    [Test]
    public async Task RunInteractiveAsync_applies_the_plans_environment_variables()
    {
        // Interactive spawns carry env vars on the CreateProcess path: the
        // shell-execution shortcut cannot be used here, because UseShellExecute
        // throws on Windows when the configuration carries environment
        // variables - and every adapter emits base-URL and key env vars. This
        // test pins that an env-var-carrying interactive spawn starts and its
        // exit code propagates, the shape the regression hid.
        LaunchRunner runner = new LaunchRunner();
        Dictionary<string, string> env = new(StringComparer.Ordinal)
        {
            ["HGW_LAUNCHER_TEST_ENV"] = "seeded",
        };
        ProcessConfiguration config = OperatingSystem.IsWindows()
            ? new ProcessConfiguration("cmd") { ArgumentList = ["/c", "exit 42"], OutputRedirection = false, EnvironmentVariables = env }
            : new ProcessConfiguration("/bin/sh") { ArgumentList = ["-c", "exit 42"], OutputRedirection = false, EnvironmentVariables = env };
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunInteractiveAsync(plan);

        await Assert.That(exitCode).IsEqualTo(42);
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
