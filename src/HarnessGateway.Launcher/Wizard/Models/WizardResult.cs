/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace HarnessGateway.Launcher.Wizard.Models;

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
public sealed record WizardResult(
    string AgentCommandName,
    string? ProviderName,
    string Endpoint,
    ProtocolKind Protocol,
    string? ApiKey,
    string? Model);