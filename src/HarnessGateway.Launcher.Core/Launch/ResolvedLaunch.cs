namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The post-decision payload an adapter plans from: a fully resolved
/// <see cref="Backend"/>, the model to launch with, and the working directory.
/// </summary>
public sealed record ResolvedLaunch(Backend Backend, string? Model, string WorkingDirectory);
