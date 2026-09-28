using HarnessGateway.Launcher.Commands;

namespace HarnessGateway.Launcher.Tests.Commands;

public class LaunchExitCodeTests
{
    [Test]
    public async Task Ok_is_zero()
    {
        await Assert.That((int)LaunchExitCode.Ok).IsEqualTo(0);
    }

    [Test]
    public async Task UsageError_is_two()
    {
        await Assert.That((int)LaunchExitCode.UsageError).IsEqualTo(2);
    }

    [Test]
    public async Task UnknownAgent_is_three()
    {
        await Assert.That((int)LaunchExitCode.UnknownAgent).IsEqualTo(3);
    }

    [Test]
    public async Task HarnessUnavailable_is_four()
    {
        await Assert.That((int)LaunchExitCode.HarnessUnavailable).IsEqualTo(4);
    }

    [Test]
    public async Task EndpointUnreachable_is_five()
    {
        await Assert.That((int)LaunchExitCode.EndpointUnreachable).IsEqualTo(5);
    }

    [Test]
    public async Task RequiredModelMissing_is_six()
    {
        await Assert.That((int)LaunchExitCode.RequiredModelMissing).IsEqualTo(6);
    }

    [Test]
    public async Task ConfigRestoreFailure_is_seven()
    {
        await Assert.That((int)LaunchExitCode.ConfigRestoreFailure).IsEqualTo(7);
    }

    [Test]
    public async Task ConfigConflict_is_ten()
    {
        await Assert.That((int)LaunchExitCode.ConfigConflict).IsEqualTo(10);
    }

    [Test]
    public async Task All_exit_codes_are_distinct()
    {
        var values = Enum.GetValues<LaunchExitCode>();
        var distinctValues = values.Distinct().ToList();
        await Assert.That(distinctValues.Count).IsEqualTo(values.Length);
    }

    [Test]
    public async Task All_exit_codes_have_names()
    {
        var values = Enum.GetValues<LaunchExitCode>();
        foreach (var value in values)
        {
            var name = Enum.GetName(value);
            await Assert.That(name).IsNotNull();
            await Assert.That(name!.Length).IsGreaterThan(0);
        }
    }
}
