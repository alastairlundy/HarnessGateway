using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed record HarnessMetadata(
    string Name,
    HarnessKind Kind,
    bool RequiresModel,
    string InstallHint,
    string? DetectionCommand,
    IReadOnlyList<string> ConfigFileCandidates,
    IReadOnlySet<ProtocolKind> SupportedProtocols);
