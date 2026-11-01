namespace HarnessGateway.Launcher.Commands;

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