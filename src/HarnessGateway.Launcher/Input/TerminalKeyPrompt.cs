/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Input.Abstractions;

namespace HarnessGateway.Launcher.Input;

internal sealed class TerminalKeyPrompt : IKeyPrompt
{
    public string? PromptForNewKey(string providerName, bool entryHasKey)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        string verb = entryHasKey ? "Replace" : "Set";
        Console.Write($"{verb} an API key for '{providerName}'? [y/N] ");
        string? answer = Console.ReadLine();

        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Console.Write("API key (input hidden, Enter to confirm): ");
        string secret = string.Empty;

        while (true)
        {
            ConsoleKeyInfo info = Console.ReadKey(intercept: true);

            if (info.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (info.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret = secret[..^1];
                }

                continue;
            }

            if (info.KeyChar != '\0')
            {
                secret += info.KeyChar;
            }
        }

        return secret.Length == 0 ? null : secret;
    }
}