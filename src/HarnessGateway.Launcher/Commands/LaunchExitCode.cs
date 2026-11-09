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
    UnknownProviderName = 8,
    UnsupportedSettingsSchema = 9,
    ConfigConflict = 10,
}