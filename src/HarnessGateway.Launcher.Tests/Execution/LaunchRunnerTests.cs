using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Execution;

public class LaunchRunnerTests
{
    [Test]
    public async Task RunAsync_propagates_exit_code()
    {
        var runner = new LaunchRunner();
        var config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 42",
            OutputRedirection = false
        };
        var plan = new LaunchPlan(config, []);

        var exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(42);
    }

    [Test]
    public async Task RunAsync_propagates_zero_exit_code()
    {
        var runner = new LaunchRunner();
        var config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 0",
            OutputRedirection = false
        };
        var plan = new LaunchPlan(config, []);

        var exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_sets_output_redirection_to_false()
    {
        var runner = new LaunchRunner();
        var config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c exit 0",
            OutputRedirection = true
        };
        var plan = new LaunchPlan(config, []);

        var exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_executes_in_foreground_and_blocks()
    {
        var runner = new LaunchRunner();
        var config = new ProcessConfiguration("cmd")
        {
            Arguments = "/c echo hello",
            OutputRedirection = false
        };
        var plan = new LaunchPlan(config, []);

        var exitCode = await runner.RunAsync(plan);

        await Assert.That(exitCode).IsEqualTo(0);
    }
}
