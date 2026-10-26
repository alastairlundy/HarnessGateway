namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The harness ran to a terminal fact, reported honestly by launch mode: its
/// exit status when the launcher waited on it
/// (<see cref="CompletedPayload.ExitStatus"/>), or the process id it handed
/// off to when it returned without waiting
/// (<see cref="CompletedPayload.HandedOff"/>).
/// </summary>
public sealed record LaunchCompleted(CompletedPayload Payload) : LaunchOutcome;
