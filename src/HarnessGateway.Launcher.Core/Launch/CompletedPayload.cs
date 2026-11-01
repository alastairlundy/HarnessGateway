namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Mode-honest terminal fact of a completed launch: exactly what actually
/// happened, by launch mode. The family is closed - a blocking run carries
/// the harness's exit status, a detached start carries the process id it
/// handed off to - so no completed result can claim an exit status that was
/// never waited on, and no sentinel exit code stands in for a handoff.
/// </summary>
/// <remarks>
/// This payload nests inside the outer <see cref="LaunchOutcome"/> family
/// whose composition (Completed, Failed, Refused) is untouched:
/// <see cref="LaunchFailed"/> and <see cref="LaunchRefused"/> deliberately
/// carry no payload of their own.
/// </remarks>
public abstract record CompletedPayload
{
    /// <summary>
    /// A blocking run's harness exit status, collected by waiting for the
    /// child process to exit.
    /// </summary>
    /// <param name="Value">The harness's exit code.</param>
    public sealed record ExitStatus(int Value) : CompletedPayload;

    /// <summary>
    /// A fire-and-forget run's detached start: the launcher returned at
    /// process handoff, so there is no exit status - only the process it
    /// handed off to.
    /// </summary>
    /// <param name="ProcessId">The detached process's id.</param>
    public sealed record HandedOff(int ProcessId) : CompletedPayload;
}
