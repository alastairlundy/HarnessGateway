namespace HarnessGateway.Launcher.Wizard;

/// <summary>The wizard aborted before collecting a launch input.</summary>
public sealed record WizardAborted(WizardAbortReason Reason) : WizardOutcome;
