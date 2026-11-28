using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// A scripted stand-in for the in-terminal ambiguity picker: it returns the
/// queued reference in order and records the title and candidates it was
/// offered, so a flow test can assert both the pick that reached the adapter
/// and the exact question the user was asked.
/// </summary>
public sealed class ScriptedModelChoicePrompt : IModelChoicePrompt
{
    private readonly Queue<string> _answers;

    public ScriptedModelChoicePrompt(params string[] answers)
    {
        _answers = new Queue<string>(answers);
    }

    public string? Title { get; private set; }

    public IReadOnlyList<string> Candidates { get; private set; } = [];

    public int CallCount { get; private set; }

    public Task<string> PickAsync(
        string title,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default)
    {
        Title = title;
        Candidates = candidates;
        CallCount++;

        if (_answers.Count == 0)
        {
            throw new InvalidOperationException(
                "ScriptedModelChoicePrompt was asked to pick but had no queued answer.");
        }

        return Task.FromResult(_answers.Dequeue());
    }
}
