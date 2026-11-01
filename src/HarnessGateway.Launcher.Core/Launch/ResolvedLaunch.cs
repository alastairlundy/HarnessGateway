namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The post-decision payload an adapter plans from: a fully resolved
/// <see cref="Provider"/>, the model to launch with, and the working directory.
/// </summary>
public sealed record ResolvedLaunch(Provider Provider, string? Model, string WorkingDirectory);
