/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Wizard.Models;

namespace HarnessGateway.Launcher.Wizard.Abstractions;

public interface IConsoleAbstraction
{
    Task<string> PromptChoiceAsync(string title, IReadOnlyList<ConsoleChoice> choices, CancellationToken cancellationToken = default);
    Task<string> PromptTextAsync(string prompt, string? defaultValue = null, bool secret = false, CancellationToken cancellationToken = default);
    void WriteMarkup(string message);
    void WriteError(string message);
}