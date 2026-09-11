namespace LmProxy.AgentLauncher.Tests.Wizard;

public sealed class TestConsole : LmProxy.AgentLauncher.Wizard.IConsoleAbstraction
{
    private readonly Queue<string> _choiceResponses = new();
    private readonly Queue<string> _textResponses = new();
    private readonly List<string> _writtenMessages = [];
    private readonly List<string> _errorMessages = [];

    public IReadOnlyList<string> WrittenMessages => _writtenMessages;
    public IReadOnlyList<string> ErrorMessages => _errorMessages;
    public int ChoiceCount { get; private set; }
    public int TextCount { get; private set; }

    public void EnqueueChoice(string response) => _choiceResponses.Enqueue(response);
    public void EnqueueText(string response) => _textResponses.Enqueue(response);

    public Task<string> PromptChoiceAsync(string title, IReadOnlyList<LmProxy.AgentLauncher.Wizard.ConsoleChoice> choices, CancellationToken cancellationToken = default)
    {
        ChoiceCount++;
        if (_choiceResponses.Count > 0)
        {
            return Task.FromResult(_choiceResponses.Dequeue());
        }
        return Task.FromResult(choices[0].Name);
    }

    public Task<string> PromptTextAsync(string prompt, string? defaultValue = null, bool secret = false, CancellationToken cancellationToken = default)
    {
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
}
