namespace HarnessGateway.Launcher.Core.Backends;

/// <summary>
/// Outcome of a backend resolution: either a resolved backend (which may be
/// null when nothing is configured at all) or a typed failure with a
/// user-facing message.
/// </summary>
public sealed record BackendResolutionResult
{
    public Backend? Backend { get; init; }

    public BackendResolutionFailure Failure { get; init; } = BackendResolutionFailure.None;

    public string? Message { get; init; }

    public bool IsSuccess => Failure == BackendResolutionFailure.None;
}