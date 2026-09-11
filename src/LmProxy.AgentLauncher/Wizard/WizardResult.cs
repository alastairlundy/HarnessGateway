using LmProxy.AgentLauncher.Core.Backends;

namespace LmProxy.AgentLauncher.Wizard;

public sealed record WizardResult(
    string AgentName,
    string Endpoint,
    ProtocolKind Protocol,
    string? ApiKey,
    string? Model);
