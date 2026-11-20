namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// The production probe: <see cref="Console.IsInputRedirected"/> and
/// <see cref="Console.IsOutputRedirected"/> over this process's own console
/// handles. Both must be false - a TUI reads keystrokes from stdin and draws
/// on stdout, so a redirect on either one makes an interactive launch
/// unusable. Reads the console only; starts nothing, writes nothing.
/// </summary>
public sealed class TerminalProbe : ITerminalProbe
{
    public bool IsInteractiveTerminal =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
