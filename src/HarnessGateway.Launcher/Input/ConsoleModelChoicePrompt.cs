/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Wizard.Abstractions;
using HarnessGateway.Launcher.Wizard.Models;

namespace HarnessGateway.Launcher.Input;

/// <summary>
/// The exe-side host for the launch flow's ambiguous-model choice. Core owns
/// the decision and the candidate list; this wraps the wizard's console
/// abstraction (and its choice-list pattern) so the picker renders through
/// the same Spectre presenter the wizard uses. A <see cref="ConsoleChoice"/>
/// carries the qualified reference as its name, and the abstraction returns
/// that name back verbatim on selection.
/// </summary>
public sealed class ConsoleModelChoicePrompt : IModelChoicePrompt
{
    private readonly IConsoleAbstraction _console;

    public ConsoleModelChoicePrompt(IConsoleAbstraction console)
    {
        _console = console;
    }

    public async Task<string> PickAsync(
        string title,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default)
    {
        List<ConsoleChoice> choices = [.. candidates.Select(candidate => new ConsoleChoice(candidate))];

        return await _console.PromptChoiceAsync(title, choices, cancellationToken)
            .ConfigureAwait(true);
    }
}
