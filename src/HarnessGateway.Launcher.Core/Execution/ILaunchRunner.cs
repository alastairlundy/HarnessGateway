namespace HarnessGateway.Launcher.Core.Execution;

public interface ILaunchRunner
{
    Task<int> RunAsync(Launch.LaunchPlan plan, CancellationToken cancellationToken = default);

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
