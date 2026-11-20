using HarnessGateway.Launcher.Core.Execution;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted terminal probe for flow tests: answers from construction, so a
/// test can pin the terminal gate either way without touching the real
/// console handles. Defaults to a present terminal - the interactive-launch
/// happy path - and only a test that means to withhold one passes
/// <c>false</c>.
/// </summary>
public sealed class FakeTerminalProbe(bool available = true) : ITerminalProbe
{
    public bool IsInteractiveTerminal => available;
}
