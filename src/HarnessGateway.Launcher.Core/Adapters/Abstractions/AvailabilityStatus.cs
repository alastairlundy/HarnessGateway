namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

/// <summary>
/// What an availability probe established about a harness.
/// </summary>
/// <remarks>
/// Three states rather than two, because the third is the honest one: a
/// probe that could not run has not found anything absent. Collapsing it
/// into <see cref="Absent"/> is what makes an installed harness report as
/// uninstalled and answer with an install instruction it does not need.
/// </remarks>
public enum AvailabilityStatus
{
    /// <summary>The probe ran and found the harness.</summary>
    Found,

    /// <summary>
    /// The probe ran and found nothing: the harness is not detectable the
    /// way this machine's probe looks for it.
    /// </summary>
    Absent,

    /// <summary>
    /// The probe itself failed, so nothing is known. Never read as
    /// <see cref="Absent"/> - there is no negative to report.
    /// </summary>
    Unknown,
}
