using Spectre.Console;

namespace HarnessGateway.Launcher.Wizard;

public sealed class SpectreConsoleAbstraction : IConsoleAbstraction
{
    public async Task<string> PromptChoiceAsync(string title, IReadOnlyList<ConsoleChoice> choices, CancellationToken cancellationToken = default)
    {
        SelectionPrompt<string> selection = new SelectionPrompt<string>()
            .Title(title)
            .PageSize(10);

        foreach (ConsoleChoice choice in choices)
        {
            string display = choice.Annotation is not null
                ? $"{choice.Name} [dim]({choice.Annotation})[/]"
                : choice.Name;
            selection.AddChoice(display);
        }

        string result = await AnsiConsole.PromptAsync(selection, cancellationToken)
            .ConfigureAwait(true);

        foreach (ConsoleChoice choice in choices)
        {
            string display = choice.Annotation is not null
                ? $"{choice.Name} [dim]({choice.Annotation})[/]"
                : choice.Name;
            
            if (string.Equals(result, display, StringComparison.OrdinalIgnoreCase))
            {
                return await Task.FromResult(choice.Name).ConfigureAwait(true);
            }
        }

        return await Task.FromResult(result).ConfigureAwait(true);
    }

    public Task<string> PromptTextAsync(string prompt, string? defaultValue = null, bool secret = false, CancellationToken cancellationToken = default)
    {
        TextPrompt<string> textPrompt = new TextPrompt<string>(prompt)
            .AllowEmpty();

        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        if (secret)
        {
            textPrompt.Secret();
        }

        string result = AnsiConsole.Prompt(textPrompt);
        return Task.FromResult(result);
    }

    public void WriteMarkup(string message)
    {
        AnsiConsole.MarkupLine(message);
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{message}[/]");
    }

    public Task WriteErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => WriteError(message), cancellationToken);
    }

    public Task WriteMarkupAsync(string message, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => WriteMarkup(message), cancellationToken);
    }
}
