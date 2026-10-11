namespace HarnessGateway.Launcher.Core.Backends;

/// <summary>
/// Outcome of a backend resolution: a closed family of exactly three
/// sealed variants. Success carries its payload non-null - a resolved
/// <see cref="Backend"/>, or proof that nothing is configured at all
/// (<see cref="EmptySettings"/>) - and failure carries the typed
/// <see cref="BackendResolutionFailure"/> plus a non-null user-facing message.
/// <see cref="IsSuccess"/> is computed from the variant, so consumers
/// pattern-match instead of null-forgiving.
/// </summary>
public abstract record BackendResolutionResult
{
    /// <summary>True for the success variants (<see cref="Resolved"/>, <see cref="EmptySettings"/>).</summary>
    public bool IsSuccess => this is Resolved or EmptySettings;

    /// <summary>A backend was resolved, possibly built from raw overrides alone.</summary>
    public sealed record Resolved(Backend Backend) : BackendResolutionResult;

    /// <summary>Success without a backend: no saved entries and no complete raw URL override.</summary>
    public sealed record EmptySettings : BackendResolutionResult;

    /// <summary>Resolution failed with a typed failure and a user-facing message.</summary>
    public sealed record Failed(BackendResolutionFailure Failure, string Message) : BackendResolutionResult;
}
