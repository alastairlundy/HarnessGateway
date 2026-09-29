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
        ProcessConfiguration config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 42",
            OutputRedirection = false
        };
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(42);
    }

    [Test]
    public async Task RunAsync_propagates_zero_exit_code()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 0",
            OutputRedirection = false
        };
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_sets_output_redirection_to_false()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 0",
            OutputRedirection = true
        };
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_executes_in_foreground_and_blocks()
    {
        LaunchRunner runner = new LaunchRunner();
        ProcessConfiguration config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c echo hello",
            OutputRedirection = false
        };
        LaunchPlan plan = new LaunchPlan(config, []);

        int exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }
}
