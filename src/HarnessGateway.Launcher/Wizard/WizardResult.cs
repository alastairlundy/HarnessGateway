namespace HarnessGateway.Launcher.Wizard;

/// <summary>
/// What the wizard collected, handed to the launch path as launch input. The
/// pipeline never persists any of it.
/// </summary>
/// <param name="AgentCommandName">
/// The harness's <c>CommandName</c> token ("copilot"), not its display label.
/// This value is passed straight to the orchestrator as the harness identifier, so
/// it must be a token the lookup accepts.
/// </param>
public sealed record WizardResult(
    string AgentCommandName,
    string Endpoint,
    ProtocolKind Protocol,
    string? ApiKey,
    string? Model);