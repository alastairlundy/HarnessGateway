namespace HarnessGateway.Launcher.Wizard;

/// <summary>The wizard collected a complete launch input.</summary>
public sealed record WizardCompleted(WizardResult Result) : WizardOutcome;
