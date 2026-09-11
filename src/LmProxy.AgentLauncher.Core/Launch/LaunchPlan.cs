using CliInvoke.Core;
using LmProxy.AgentLauncher.Core.Configuration;

namespace LmProxy.AgentLauncher.Core.Launch;

public sealed record LaunchPlan(ProcessConfiguration Process, IReadOnlyList<ConfigurationAction> ConfigurationActions);
