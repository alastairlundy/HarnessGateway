namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The post-decision payload an adapter plans from: the resolved
/// two-case <see cref="LaunchResolution"/> family handed over directly -
/// no stored-looking provider is ever synthesized for a raw-URL launch -
/// the launch-scoped API key resolved from the credential store
/// (never the at-rest reference; <c>null</c> when the launch is keyless),
/// the working directory, and the shared emission planner the adapter
/// maps the case through.
/// </summary>
public sealed record ResolvedLaunch(
    LaunchResolution Launch,
    string? ApiKey,
    string WorkingDirectory,
    IProtocolEmissionPlanner EmissionPlanner);
