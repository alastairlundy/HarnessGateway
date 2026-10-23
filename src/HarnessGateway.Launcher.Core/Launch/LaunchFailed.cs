namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The launch failed before an exit status could be produced; carries the typed
/// cause and a user-facing message.
/// </summary>
public sealed record LaunchFailed(LaunchFailureKind Kind, string Message) : LaunchOutcome;
