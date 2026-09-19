using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Wizard;

public sealed record WizardResult(
    string AgentName,
    string Endpoint,
    ProtocolKind Protocol,
    string? ApiKey,
    string? Model);
