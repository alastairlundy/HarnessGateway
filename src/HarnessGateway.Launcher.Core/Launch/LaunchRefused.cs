namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The launch was refused before anything ran; carries the typed reason and a
/// user-facing message. Refusals are user-rectifiable, never internal faults.
/// </summary>
public sealed record LaunchRefused(LaunchRefusalKind Kind, string Message) : LaunchOutcome;
