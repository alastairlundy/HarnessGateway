using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Providers;

namespace HarnessGateway.Launcher.Tests.Launch;

/// <summary>
/// The shared refusal prose. Pinned because it is what a user reads when a
/// launch cannot run: the PATH caveat must survive every future edit, and
/// the failed-check message must never grow an install instruction.
/// </summary>
/// <remarks>
/// Fixture hints are the placeholder <c>&lt;install hint&gt;</c>, never a
/// package name: a test must not name an npm package that may not exist or
/// that someone could run. Real hints belong to the adapters that declare
/// them, not here.
/// </remarks>
public class AvailabilityMessagesTests
{
    [Test]
    public async Task NotInstalled_appends_the_path_caveat_to_a_hint_without_a_full_stop()
    {
        HarnessMetadata metadata = CreateMetadata();

        string message = AvailabilityMessages.NotInstalled(metadata);

        await Assert.That(message)
            .IsEqualTo("<install hint>. " + AvailabilityMessages.NotOnPath);
    }

    [Test]
    public async Task Not_installed_does_not_double_the_full_stop_of_a_hint_that_already_ends_with_one()
    {
        // The same sentence with and without its trailing full stop must
        // produce the same message: the joiner owns the punctuation, so a
        // hint that already ends a sentence never prints "..".
        HarnessMetadata withStop = CreateMetadata(installHint: "<install hint>.");
        HarnessMetadata withoutStop = CreateMetadata();

        await Assert.That(AvailabilityMessages.NotInstalled(withStop))
            .IsEqualTo(AvailabilityMessages.NotInstalled(withoutStop));
        await Assert.That(AvailabilityMessages.NotInstalled(withStop))
            .IsEqualTo("<install hint>. " + AvailabilityMessages.NotOnPath);
    }

    [Test]
    public async Task CheckFailed_names_the_harness_the_cause_and_the_detection_command()
    {
        HarnessMetadata metadata = CreateMetadata();

        string message = AvailabilityMessages.CheckFailed(
            metadata, "Win32Exception: Access is denied.");

        await Assert.That(message).Contains("Could not check whether Codex CLI is installed");
        await Assert.That(message).Contains("Win32Exception: Access is denied.");
        await Assert.That(message).Contains("checked with 'codex --version'");
    }

    [Test]
    public async Task CheckFailed_carries_no_install_hint_because_nothing_was_established()
    {
        // The whole point of the third state: a probe that could not run
        // found nothing, so advising an install would invent a finding.
        HarnessMetadata metadata = CreateMetadata();

        string message = AvailabilityMessages.CheckFailed(metadata, "the check failed");

        await Assert.That(message).DoesNotContain("<install hint>");
        await Assert.That(message).DoesNotContain(AvailabilityMessages.NotOnPath);
    }

    private static HarnessMetadata CreateMetadata(string installHint = "<install hint>") =>
        new(
            Name: "Codex CLI",
            CommandName: "codex",
            [],
            HarnessKind.Cli,
            RequiresModel: true,
            InstallHint: installHint,
            DetectionCommand: "codex --version",
            [],
            new HashSet<ProtocolKind> { ProtocolKind.OpenAiResponses },
            new HashSet<ConfigurationSurface>(),
            []);
}
