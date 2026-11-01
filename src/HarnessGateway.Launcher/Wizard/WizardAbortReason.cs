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
    DanglingDefaultProvider,

    /// <summary>The default-backend pointer is empty while saved entries exist. Maps to exit 8.</summary>
    AbsentDefaultProvider,

    /// <summary>No agent harness is installed on this machine. Maps to exit 4.</summary>
    NoHarnessInstalled,

    /// <summary>No harness (installed or registered) supports the chosen protocol. Maps to exit 3.</summary>
    NoHarnessSupportsProtocol,

    /// <summary>The platform credential store failed. Maps to exit 7.</summary>
    CredentialStoreError,
}
