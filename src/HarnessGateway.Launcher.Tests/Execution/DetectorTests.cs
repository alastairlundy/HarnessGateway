using System.Diagnostics;
using HarnessGateway.Launcher.Core.Execution;

namespace HarnessGateway.Launcher.Tests.Execution;

/// <summary>
/// Pins the per-OS running-instance detectors against the machine they
/// actually run on: the OS's own implementation is exercised directly, and
/// every cross-OS behavior (the Windows <c>.exe</c> normalization, the Linux
/// case rule, the macOS case rule) sits behind that OS's own guard so the
/// Linux gate stays green and nothing here is flaky elsewhere. Detection is
/// read-only process enumeration - no test here starts a process.
/// </summary>
public class DetectorTests
{
    // Mirrors Program.cs's wiring: the shipped binary carries the detector
    // for the OS it runs on, so that is the one under test on this machine.
    private static IRunningInstanceDetector CurrentOsDetector()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsInstanceDetector();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsInstanceDetector();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxInstanceDetector();
        }

        throw new PlatformNotSupportedException(
            "Running-instance detection ships Windows, macOS, and Linux implementations only.");
    }

    // --- The shared input contract, on the running OS's detector ---

    [Test]
    public async Task Empty_name_list_is_an_immediate_miss()
    {
        (bool found, IReadOnlyList<int> processIds) = CurrentOsDetector().DetectRunningInstances([]);

        await Assert.That(found).IsFalse();
        await Assert.That(processIds).IsEmpty();
    }

    [Test]
    public async Task Blank_only_name_list_is_an_immediate_miss()
    {
        (bool found, IReadOnlyList<int> processIds) = CurrentOsDetector()
            .DetectRunningInstances(["", "   "]);

        await Assert.That(found).IsFalse();
        await Assert.That(processIds).IsEmpty();
    }

    [Test]
    public async Task The_current_process_is_detected_and_carries_its_id()
    {
        string currentName = Process.GetCurrentProcess().ProcessName;

        (bool found, IReadOnlyList<int> processIds) = CurrentOsDetector()
            .DetectRunningInstances([currentName]);

        // The one guaranteed-live name on any runner: this very process.
        // Whatever the OS's name-matching rules are, they must at least
        // match the name the same OS reports for a process that exists.
        await Assert.That(found).IsTrue();
        await Assert.That(processIds).Contains(Environment.ProcessId);
    }

    [Test]
    public async Task Blank_names_are_ignored_alongside_a_live_one()
    {
        string currentName = Process.GetCurrentProcess().ProcessName;

        (bool found, IReadOnlyList<int> processIds) = CurrentOsDetector()
            .DetectRunningInstances(["", "   ", currentName]);

        await Assert.That(found).IsTrue();
        await Assert.That(processIds).Contains(Environment.ProcessId);
    }

    [Test]
    public async Task A_name_matching_no_running_process_reports_a_miss()
    {
        (bool found, IReadOnlyList<int> processIds) = CurrentOsDetector()
            .DetectRunningInstances(["harnessgateway-definitely-absent-process"]);

        await Assert.That(found).IsFalse();
        await Assert.That(processIds).IsEmpty();
    }

    // --- The per-OS matching rules, each behind its own OS guard ---

    [Test]
    public async Task Windows_normalizes_a_declared_exe_suffix_to_the_image_name()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // The Windows rule only exists on Windows.
        }

        string currentName = Process.GetCurrentProcess().ProcessName;

        (bool found, IReadOnlyList<int> processIds) = new WindowsInstanceDetector()
            .DetectRunningInstances([$"{currentName}.exe"]);

        await Assert.That(found).IsTrue();
        await Assert.That(processIds).Contains(Environment.ProcessId);
    }

    [Test]
    public async Task Linux_matches_declared_names_case_sensitively()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // The Linux rule only exists on Linux.
        }

        string currentName = Process.GetCurrentProcess().ProcessName;

        // The test process's name always carries lowercase letters, so its
        // uppercase form is a genuinely different ordinal string.
        (bool found, _) = new LinuxInstanceDetector()
            .DetectRunningInstances([currentName.ToUpperInvariant()]);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task MacOS_matches_declared_names_case_insensitively()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return; // The macOS rule only exists on macOS.
        }

        string currentName = Process.GetCurrentProcess().ProcessName;

        (bool found, IReadOnlyList<int> processIds) = new MacOsInstanceDetector()
            .DetectRunningInstances([currentName.ToUpperInvariant()]);

        await Assert.That(found).IsTrue();
        await Assert.That(processIds).Contains(Environment.ProcessId);
    }
}
