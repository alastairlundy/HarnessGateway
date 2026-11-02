namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Outcome of a launch resolution: a closed family of exactly three
/// sealed variants. Success carries its payload non-null - a resolved
/// <see cref="LaunchResolution"/> case (stored provider or keyless ad-hoc),
/// or proof that nothing is configured at all
/// (<see cref="EmptySettings"/>) - and failure carries the typed
/// <see cref="ProviderResolutionFailure"/> plus a non-null user-facing message.
/// <see cref="IsSuccess"/> is computed from the variant, so consumers
/// pattern-match instead of null-forgiving.
/// </summary>
public abstract record ProviderResolutionResult
{
    /// <summary>True for the success variants (<see cref="Resolved"/>, <see cref="EmptySettings"/>).</summary>
    public bool IsSuccess => this is Resolved or EmptySettings;

    /// <summary>
    /// A launch resolved to its <see cref="LaunchResolution"/> case: the
    /// provider case when a stored entry backs it, the ad-hoc case for a
    /// raw-URL launch with no saved provider.
    /// </summary>
    public sealed record Resolved(LaunchResolution Launch) : ProviderResolutionResult;

    /// <summary>Success without a backend: no saved entries and no complete raw URL override.</summary>
    public sealed record EmptySettings : ProviderResolutionResult;

    /// <summary>
    /// Resolution failed with a typed failure and a user-facing message. The
    /// two default-pointer failures are distinct:
    /// <see cref="ProviderResolutionFailure.DanglingDefaultProvider"/> (the
    /// default-backend pointer is set but names no saved entry) versus
    /// <see cref="ProviderResolutionFailure.AbsentDefaultProvider"/> (the
    /// pointer is empty while saved entries exist — no first-entry fallback).
    /// </summary>
    public sealed record Failed(ProviderResolutionFailure Failure, string Message) : ProviderResolutionResult;
}
