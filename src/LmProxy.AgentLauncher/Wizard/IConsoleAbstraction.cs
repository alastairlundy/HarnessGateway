namespace LmProxy.AgentLauncher.Wizard;

public interface IConsoleAbstraction
{
    Task<string> PromptChoiceAsync(string title, IReadOnlyList<ConsoleChoice> choices, CancellationToken cancellationToken = default);
    Task<string> PromptTextAsync(string prompt, string? defaultValue = null, bool secret = false, CancellationToken cancellationToken = default);
    void WriteMarkup(string message);
    void WriteError(string message);
}

public sealed record ConsoleChoice(string Name, string? Annotation = null);
