namespace HarnessGateway.Launcher.Core.Update;

/// <summary>
/// What one update check concluded: the three compared
/// outcomes plus the cooldown skip. The exit-code mapping lives in the CLI
/// layer - this enum stays free of command-line concerns.
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>The latest published release is not newer than this build.</summary>
    UpToDate,

    /// <summary>The latest published release is newer than this build.</summary>
    UpdateAvailable,

    /// <summary>The network call failed or the response could not be parsed.</summary>
    CheckFailed,

    /// <summary>The cooldown window is still open; no network call was made.</summary>
    CooldownActive,
}
