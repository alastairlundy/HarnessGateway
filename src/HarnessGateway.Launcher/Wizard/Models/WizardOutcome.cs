namespace HarnessGateway.Launcher.Wizard.Models;

/// <summary>
/// Closed outcome of a wizard run: either a completed <see cref="WizardResult"/>
/// ready to hand to the launch flow, or an abort with a typed reason.
/// </summary>
public abstract record WizardOutcome
{
    /// <summary>
    /// Convenience conversion so callers that only care about the collected result (and treat an abort as <c language="">null</c>)
    /// keep working. Production code should pattern-match the derived types to recover the abort reason.
    /// </summary>
    public static implicit operator WizardResult?(WizardOutcome outcome) =>
        outcome is WizardCompleted completed ? completed.Result : null;
}