namespace HarnessGateway.Launcher.Wizard;

/// <summary>
/// Why a wizard run aborted before producing a <see cref="WizardResult"/>. Each
/// reason maps to the same exit code the direct <c>launch</c> path returns for
/// the equivalent condition, so the wizard and direct mode agree.
/// </summary>
public enum WizardAbortReason
{
    /// <summary>The user cancelled (Ctrl+C / backed out). Maps to exit 0.</summary>
    Cancelled,

    /// <summary>The default-backend pointer is set but names no saved entry. Maps to exit 8.</summary>
    DanglingDefaultBackend,

    /// <summary>The default-backend pointer is empty while saved entries exist. Maps to exit 8.</summary>
    AbsentDefaultBackend,

    /// <summary>No agent harness is installed on this machine. Maps to exit 4.</summary>
    NoHarnessInstalled,

    /// <summary>No harness (installed or registered) supports the chosen protocol. Maps to exit 3.</summary>
    NoHarnessSupportsProtocol,

    /// <summary>The platform credential store failed. Maps to exit 7.</summary>
    CredentialStoreError,
}

/// <summary>
/// Closed outcome of a wizard run: either a completed <see cref="WizardResult"/>
/// ready to hand to the launch flow, or an abort with a typed reason.
/// </summary>
public abstract record WizardOutcome
{
    /// <summary>
    /// Convenience conversion so callers that only care about the collected
    /// result (and treat an abort as <c>null</c>) keep working. Production
    /// code should pattern-match the derived types to recover the abort reason.
    /// </summary>
    public static implicit operator WizardResult?(WizardOutcome outcome) =>
        outcome is WizardCompleted completed ? completed.Result : null;
}

/// <summary>The wizard collected a complete launch input.</summary>
public sealed record WizardCompleted(WizardResult Result) : WizardOutcome;

/// <summary>The wizard aborted before collecting a launch input.</summary>
public sealed record WizardAborted(WizardAbortReason Reason) : WizardOutcome;
