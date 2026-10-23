namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The harness ran and this is its exit status.
/// </summary>
public sealed record LaunchCompleted(int ExitStatus) : LaunchOutcome;
