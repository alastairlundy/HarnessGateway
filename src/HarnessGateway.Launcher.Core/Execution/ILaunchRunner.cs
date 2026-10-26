namespace HarnessGateway.Launcher.Core.Execution;

public interface ILaunchRunner
{
    Task<int> RunAsync(Launch.LaunchPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the plan's process to exit with no timeout: the wait's upper bound
    /// is the application exiting on its own or the caller cancelling, never a
    /// clock. <see cref="RunAsync"/>'s default exit configuration carries the
    /// process library's 2-minute timeout - a bound that would force-terminate
    /// a GUI application the user is still working in - so a blocking GUI run
    /// takes this member instead. Start failures and wait faults throw; the
    /// flow is what folds them into a launch outcome.
    /// </summary>
    Task<int> RunWithoutTimeoutAsync(Launch.LaunchPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the plan's process detached and returns its process id without
    /// waiting for it to exit - the counterpart to <see cref="RunAsync"/>, which
    /// blocks on the child and returns its exit code. There is deliberately no
    /// cancellation token: a detached start is not cancelable, and the
    /// synchronous library call it wraps takes none. Start failures throw; the
    /// flow is what folds them into a launch outcome.
    /// </summary>
    int Handoff(Launch.LaunchPlan plan);
}
