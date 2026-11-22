/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace HarnessGateway.Launcher.Wizard.Models;

/// <summary>
/// One selectable line in a wizard prompt: the raw display name plus its raw
/// annotation text, and the structured state the presenter needs to colour the
/// line without parsing that text back apart. The state fields are safe
/// defaults - call sites that only carry text keep compiling and render as
/// before.
/// </summary>
public sealed record ConsoleChoice(
    string Name,
    string? Annotation = null,
    bool Unavailable = false,
    string? IncompatibleProtocols = null);