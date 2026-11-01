namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// The one Core seam for asking the operating system whether a harness's
/// application instance is already running. It is the sole input to the
/// running-instance confirmation prompt: detection answers presence and
/// identity, and the launch flow decides what a live instance means.
/// </summary>
/// <remarks>
/// The input list is the match vocabulary - it comes from
/// <c>HarnessMetadata.InstanceProcessNames</c>, the same declared list install
/// discovery reads, so no harness name is ever hard-coded in Core. Detection
/// consults the operating system only: read-only process enumeration, no
/// app-specific protocols, and no process starts. Per-operating-system
/// implementations are marked to the platform they serve, so the binary
/// shipped for the running OS supplies its own; the flow consumes this
/// interface as a constructor-injected collaborator so tests fake it.
/// </remarks>
public interface IRunningInstanceDetector
{
    /// <summary>
    /// Reports whether any process whose name matches an entry of
    /// <paramref name="processNames"/> is running, carrying every matched
    /// process's id - not just the first. Name-matching case handling is an
    /// operating-system fact resolved inside each implementation.
    /// </summary>
    /// <param name="processNames">
    /// The harness's declared identity list, from
    /// <c>HarnessMetadata.InstanceProcessNames</c>. Empty input is an
    /// immediate miss: <c>(Found: false, ProcessIds: [])</c>.
    /// </param>
    (bool Found, IReadOnlyList<int> ProcessIds) DetectRunningInstances(
        IReadOnlyList<string> processNames);
}
