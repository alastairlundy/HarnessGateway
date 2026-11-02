using HarnessGateway.Launcher.Core.Providers;

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
/// <param name="ProviderName">
/// The identifier of the saved provider the user selected, or <c>null</c> when
/// the session created an ad-hoc entry. Display labels are never user input: the
/// wizard resolves the picked display name back to this identifier.
/// </param>
/// <param name="Models">
/// The model entries the wizard emitted, shaped like the settings schema
/// (<see cref="ModelEntry"/>), each carrying its protocol override (null means
/// the provider default applies). Names are unique within the list.
/// </param>
public sealed record WizardResult(
    string AgentCommandName,
    string? ProviderName,
    string Endpoint,
    ProtocolKind Protocol,
    string? ApiKey,
    string? Model,
    IReadOnlyList<ModelEntry> Models);