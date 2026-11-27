namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

/// <summary>
/// The answer to "is this harness available?": the
/// <see cref="Status"/> the probe established, and - when the probe itself
/// failed - why.
/// </summary>
/// <param name="Status">What the probe established.</param>
/// <param name="Cause">
/// Why the probe failed. Meaningful only for
/// <see cref="AvailabilityStatus.Unknown"/>; null for the other two states,
/// which are answers rather than failures.
/// </param>
public readonly record struct AvailabilityResult(AvailabilityStatus Status, string? Cause = null)
{
    /// <summary>The probe ran and found the harness.</summary>
    public static AvailabilityResult Found { get; } = new(AvailabilityStatus.Found);

    /// <summary>The probe ran and found nothing.</summary>
    public static AvailabilityResult Absent { get; } = new(AvailabilityStatus.Absent);

    /// <summary>The probe could not run: <paramref name="cause"/> says why.</summary>
    public static AvailabilityResult Unknown(string cause) => new(AvailabilityStatus.Unknown, cause);
}
