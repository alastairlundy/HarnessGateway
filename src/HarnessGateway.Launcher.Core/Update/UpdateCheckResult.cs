namespace HarnessGateway.Launcher.Core.Update;

/// <summary>
/// One update-check outcome plus the data needed to report it: the remaining
/// window when <see cref="Outcome"/> is <see cref="UpdateCheckOutcome.CooldownActive"/>,
/// the published tag when one was read, and a failure detail when it is
/// <see cref="UpdateCheckOutcome.CheckFailed"/>. Message wording stays with
/// the CLI - only these values are testable contract (T017).
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    TimeSpan CooldownRemaining = default,
    string? LatestVersion = null,
    string? FailureDetail = null);
