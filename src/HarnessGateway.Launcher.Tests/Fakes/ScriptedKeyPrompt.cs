using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Input.Abstractions;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IKeyPrompt"/> for the <c>provider add</c> prompt
/// seam: records how often the prompt would have been shown and returns the
/// scripted value (null = the user declined, so the entry stays keyless).
/// </summary>
public sealed class ScriptedKeyPrompt : IKeyPrompt
{
    private readonly string? _key;

    public ScriptedKeyPrompt(string? key)
    {
        _key = key;
    }

    public int CallCount { get; private set; }

    public string? PromptForNewKey(string providerName, bool entryHasKey)
    {
        CallCount++;
        return _key;
    }
}
