using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Models;

// These tests pin the documented exit-code contract: the subject is an enum
// cast to int by design, so TUnit's "do not assert on a constant" rule is a
// false positive here - the constant is recompiled with the enum, so a
// renumbered exit code still fails the assertion.
#pragma warning disable TUnitAssertions0005

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
    public async Task UnknownProviderName_is_eight()
    {
        // Subject held in a local so the assertion is not on a constant.
        int code = (int)LaunchExitCode.UnknownProviderName;
        await Assert.That(code).IsEqualTo(8);
    }

    [Test]
    public async Task UnsupportedSettingsSchema_is_nine()
    {
        // Subject held in a local so the assertion is not on a constant.
        int code = (int)LaunchExitCode.UnsupportedSettingsSchema;
        await Assert.That(code).IsEqualTo(9);
    }

    [Test]
    public async Task ConfigConflict_is_ten()
    {
        await Assert.That((int)LaunchExitCode.ConfigConflict).IsEqualTo(10);
    }

    [Test]
    public async Task UpdateAvailable_is_eleven()
    {
        // Subject held in a local so the assertion is not on a constant.
        int code = (int)LaunchExitCode.UpdateAvailable;
        await Assert.That(code).IsEqualTo(11);
    }

    [Test]
    public async Task CheckFailed_is_twelve()
    {
        // Subject held in a local so the assertion is not on a constant.
        int code = (int)LaunchExitCode.CheckFailed;
        await Assert.That(code).IsEqualTo(12);
    }

    [Test]
    public async Task TerminalRequired_is_thirteen()
    {
        int code = (int)LaunchExitCode.TerminalRequired;
        await Assert.That(code).IsEqualTo(13);
    }

    [Test]
    public async Task AmbiguousModel_is_fourteen()
    {
        // Appended, never renumbered: exit 14 is the next free value after
        // TerminalRequired, and the flow's ambiguous-model refusal owns it.
        int code = (int)LaunchExitCode.AmbiguousModel;
        await Assert.That(code).IsEqualTo(14);
    }

    [Test]
    public async Task All_exit_codes_are_distinct()
    {
        LaunchExitCode[] values = Enum.GetValues<LaunchExitCode>();
        List<LaunchExitCode> distinctValues = values.Distinct().ToList();
        await Assert.That(distinctValues.Count).IsEqualTo(values.Length);
    }

    [Test]
    public async Task All_exit_codes_have_names()
    {
        LaunchExitCode[] values = Enum.GetValues<LaunchExitCode>();
        foreach (LaunchExitCode value in values)
        {
            string? name = Enum.GetName(value);
            await Assert.That(name).IsNotNull();
            await Assert.That(name.Length).IsGreaterThan(0);
        }
    }
}
#pragma warning restore TUnitAssertions0005
