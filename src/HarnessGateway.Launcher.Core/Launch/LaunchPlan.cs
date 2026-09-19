using HarnessGateway.Launcher.Core.Configuration;

namespace HarnessGateway.Launcher.Core.Launch;

public sealed record LaunchPlan(ProcessConfiguration Process, 
    IReadOnlyList<ConfigurationAction> ConfigurationActions);
