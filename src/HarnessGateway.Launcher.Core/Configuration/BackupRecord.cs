namespace HarnessGateway.Launcher.Core.Configuration;

public sealed record BackupRecord(
    string TargetPath,
    byte[]? OriginalBytes,
    bool Persisted);