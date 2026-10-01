namespace HarnessGateway.Launcher.Tests.Wizard;

public sealed record PromptLogEntry(
    string Title,
    IReadOnlyList<HarnessGateway.Launcher.Wizard.ConsoleChoice> Choices);

public sealed class TestConsole : HarnessGateway.Launcher.Wizard.IConsoleAbstraction
{
    private readonly Queue<string> _choiceResponses = new();
    private readonly Queue<string> _textResponses = new();
    private readonly List<string> _writtenMessages = [];
    private readonly List<string> _errorMessages = [];
    private readonly List<PromptLogEntry> _prompts = [];

    public IReadOnlyList<string> WrittenMessages => _writtenMessages;
    public IReadOnlyList<string> ErrorMessages => _errorMessages;
    public IReadOnlyList<PromptLogEntry> Prompts => _prompts;
    public int ChoiceCount { get; private set; }
    public int TextCount { get; private set; }

    public void EnqueueChoice(string response) => _choiceResponses.Enqueue(response);
    public void EnqueueText(string response) => _textResponses.Enqueue(response);

    public Task<string> PromptChoiceAsync(string title, IReadOnlyList<HarnessGateway.Launcher.Wizard.ConsoleChoice> choices, CancellationToken cancellationToken = default)
    {
        _prompts.Add(new PromptLogEntry(title, choices));
        ChoiceCount++;
        if (_choiceResponses.Count > 0)
        {
            return Task.FromResult(_choiceResponses.Dequeue());
        }
        return Task.FromResult(choices[0].Name);
    }

    public Task<string> PromptTextAsync(string prompt, string? defaultValue = null, bool secret = false, CancellationToken cancellationToken = default)
    {
        _prompts.Add(new PromptLogEntry(prompt, []));
        TextCount++;
        if (_textResponses.Count > 0)
        {
            return Task.FromResult(_textResponses.Dequeue());
        }
        return Task.FromResult(defaultValue ?? string.Empty);
    }

    public void WriteMarkup(string message)
    {
        _writtenMessages.Add(message);
    }

    public void WriteError(string message)
    {
        _errorMessages.Add(message);
    }

    public Task WriteErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        WriteError(message);
        return Task.CompletedTask;
    }

    public Task WriteMarkupAsync(string message, CancellationToken cancellationToken = default)
    {
        WriteMarkup(message);
        return Task.CompletedTask;
    }
}
