using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Launch;

/// <summary>
/// Pins the shape of the declarative submission itself: the confirmation
/// override is a two-state bool whose absent-flag value is false, and the
/// wizard handoff (which builds its request without ever naming the field)
/// therefore cannot produce an asserted confirmation.
/// </summary>
public class LaunchRequestTests
{
    [Test]
    public async Task Confirm_running_defaults_to_false_when_the_flag_is_absent()
    {
        // No tri-state: "unanswered" is not representable. The parse layer
        // only ever sets this to true, and nothing else constructs it true.
        LaunchRequest request = new("TestAgent", null, null, null, null, [], null);

        await Assert.That(request.ConfirmRunning).IsFalse();
    }

    [Test]
    public async Task Confirm_running_is_carried_once_named_at_construction()
    {
        LaunchRequest request = new(
            "TestAgent", null, null, null, null, [], null, ConfirmRunning: true);

        await Assert.That(request.ConfirmRunning).IsTrue();
    }

    [Test]
    public async Task Non_interactive_defaults_to_false_and_is_carried_when_named()
    {
        LaunchRequest absent = new("TestAgent", null, null, null, null, [], null);
        LaunchRequest present = new("TestAgent", null, null, null, null, [], null, NonInteractive: true);

        await Assert.That(absent.NonInteractive).IsFalse();
        await Assert.That(present.NonInteractive).IsTrue();
    }
}
