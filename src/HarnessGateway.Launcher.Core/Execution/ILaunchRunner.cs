namespace HarnessGateway.Launcher.Core.Execution;

public interface ILaunchRunner
{
    Task<int> RunAsync(Launch.LaunchPlan plan, CancellationToken cancellationToken = default);
}
