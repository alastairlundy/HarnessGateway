namespace HarnessGateway.Launcher.Core;

/// <summary>
/// The shared Try-outcome type for Core: a lookup either succeeded and carries a
/// value, or failed and carries a non-nullable error describing why. Expected
/// failures (missing entry, unreachable store) are data here rather than
/// exceptions, so callers branch on <see cref="Success"/> instead of catching.
/// </summary>
/// <typeparam name="T">The success payload type.</typeparam>
public sealed record Result<T>(bool Success, T Value, string Error)
{
    /// <summary>Creates a successful outcome. <see cref="Error"/> is set to the empty string.</summary>
    public static Result<T> Ok(T value) => new(true, value, string.Empty);

    /// <summary>Creates a failed outcome with a non-empty, secret-free description.</summary>
    public static Result<T> Fail(string error) => new(Success: false, default!, error);
}

/// <summary>Non-generic entry points for the <see cref="Result{T}"/> factories.</summary>
public static class Result
{
    /// <summary>Creates a successful <see cref="Result{T}"/> carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    /// <summary>Creates a failed <see cref="Result{T}"/> carrying <paramref name="error"/>.</summary>
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
}
