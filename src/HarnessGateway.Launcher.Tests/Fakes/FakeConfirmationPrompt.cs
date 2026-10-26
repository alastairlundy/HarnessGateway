using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted confirmation prompt for flow tests: returns a fixed answer,
/// records every question it was asked, and never touches the real console.
/// </summary>
public sealed class FakeConfirmationPrompt : IConfirmationPrompt
{
    private readonly bool _confirmed;

    /// <param name="confirmed">The answer to give every question.</param>
    public FakeConfirmationPrompt(bool confirmed = false)
    {
        _confirmed = confirmed;
    }

    /// <summary>Every question presented, in order.</summary>
    public List<string> PromptedMessages { get; } = [];

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
    {
        PromptedMessages.Add(message);
        return Task.FromResult(_confirmed);
    }
}
