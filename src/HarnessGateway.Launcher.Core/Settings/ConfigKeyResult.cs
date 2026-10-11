namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// Outcome of a typed config-key read or write. On success a get carries the
/// scalar <see cref="Value"/>; a set carries the updated <see cref="Settings"/>
/// snapshot to persist (plus <see cref="AlreadySatisfied"/> for a no-op
/// pointer write). On failure <see cref="Message"/> is the user-facing line.
/// </summary>
public sealed record ConfigKeyResult
{
    public bool IsSuccess => Failure == ConfigKeyFailure.None;

    /// <summary>User-facing confirmation (set) or error (failure).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The scalar value read by a successful get.</summary>
    public string? Value { get; init; }

    /// <summary>Updated settings to persist after a successful set.</summary>
    public LauncherSettings? Settings { get; init; }

    /// <summary>True when a set matched the current value and needs no save.</summary>
    public bool AlreadySatisfied { get; init; }

    public ConfigKeyFailure Failure { get; init; } = ConfigKeyFailure.None;
}