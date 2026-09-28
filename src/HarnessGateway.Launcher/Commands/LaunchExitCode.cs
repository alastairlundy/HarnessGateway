namespace HarnessGateway.Launcher.Commands;

public enum LaunchExitCode
{
    Ok = 0,
    UsageError = 2,
    UnknownAgent = 3,
    HarnessUnavailable = 4,
    EndpointUnreachable = 5,
    RequiredModelMissing = 6,
    ConfigRestoreFailure = 7,
    UnknownBackendName = 8,
    ConfigConflict = 10
}