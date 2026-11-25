/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace HarnessGateway.Launcher.Wizard.Models;

/// <summary>
/// The outcome of one <see cref="ModelStep"/> run: the selected model name (or <see langword="null"/> when skipped/unavailable)
/// and the effective protocol for that selection (null when the selection carries no protocol finding). The wizard
/// is write-free: model entries are saved only through the <c>provider model</c> verbs, never here.
/// </summary>
public sealed record ModelSelection(
    string? Model,
    ProtocolKind? Protocol);