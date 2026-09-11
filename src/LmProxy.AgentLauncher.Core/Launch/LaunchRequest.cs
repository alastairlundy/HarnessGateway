using LmProxy.AgentLauncher.Core.Backends;

namespace LmProxy.AgentLauncher.Core.Launch;

public sealed record LaunchRequest(Backend Backend, string? Model, string WorkingDirectory);
