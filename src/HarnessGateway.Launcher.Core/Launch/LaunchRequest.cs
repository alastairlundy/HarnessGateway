namespace HarnessGateway.Launcher.Core.Launch;

public sealed record LaunchRequest(Backend Backend, string? Model, string WorkingDirectory);
