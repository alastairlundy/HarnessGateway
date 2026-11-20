namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// Whether this process is attached to an interactive terminal on the
/// handles a child would inherit. The launch flow reads it before starting
/// an interactive harness: a child handed pipes instead of a terminal would
/// render into handles nobody can read (or, for a harness that detects the
/// difference, refuse to run at all).
/// </summary>
public interface ITerminalProbe
{
    /// <summary>
    /// <c>true</c> when both stdin and stdout are this process's own
    /// terminal - i.e. the standard handles a child inherits here are the
    /// ones a TUI needs.
    /// </summary>
    bool IsInteractiveTerminal { get; }
}
