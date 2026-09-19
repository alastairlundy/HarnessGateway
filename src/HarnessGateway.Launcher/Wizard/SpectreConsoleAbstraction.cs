using Spectre.Console;

namespace HarnessGateway.Launcher.Wizard;

public sealed class SpectreConsoleAbstraction : IConsoleAbstraction
{
    public Task<string> PromptChoiceAsync(string title, IReadOnlyList<ConsoleChoice> choices, CancellationToken cancellationToken = default)
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

        string result = AnsiConsole.Prompt(selection);

        foreach (ConsoleChoice choice in choices)
        {
            string display = choice.Annotation is not null
                ? $"{choice.Name} [dim]({choice.Annotation})[/]"
                : choice.Name;
            if (result == display)
            {
                return Task.FromResult(choice.Name);
            }
        }

        return Task.FromResult(result);
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
}
