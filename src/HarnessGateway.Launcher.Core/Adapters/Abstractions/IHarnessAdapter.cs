using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

public interface IHarnessAdapter
{
    HarnessMetadata Metadata { get; }

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default);
}