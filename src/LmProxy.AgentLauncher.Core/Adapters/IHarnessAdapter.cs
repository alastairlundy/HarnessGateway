using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Core.Adapters;

public interface IHarnessAdapter
{
    HarnessMetadata Metadata { get; }

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default);
}
