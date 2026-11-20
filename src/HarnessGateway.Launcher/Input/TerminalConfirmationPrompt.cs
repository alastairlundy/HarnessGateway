/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Input;

/// <summary>
/// The terminal-backed <see cref="IConfirmationPrompt"/>: it prints the
/// flow-composed question and reads the answer from the console.
/// </summary>
/// <remarks>
/// The unanswerable case is part of the contract, not an error: with
/// redirected standard input or output there is no interactive terminal to
/// ask, so this reports a decline instead of prompting into a pipe. The flow
/// maps that to the same user-rectifiable refusal an explicit decline gets,
/// which is what makes scripted non-interactive launches deterministic.
/// </remarks>
internal sealed class TerminalConfirmationPrompt : IConfirmationPrompt
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            // Nothing can answer: decline without prompting.
            return Task.FromResult(false);
        }

        Console.Out.WriteLine(message);
        string? answer = Console.ReadLine();
        bool confirmed = answer is not null
                         && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                             || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(confirmed);
    }
}
