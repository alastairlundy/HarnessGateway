namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// Typed failure classes for key sourcing. Callers map these to their own
/// exit codes; the resolver itself never exits.
/// </summary>
public enum ApiKeySourceFailure
{
    None = 0,

    /// <summary>Both the inline key flag and the environment-variable flag were given.</summary>
    FlagConflict,

    /// <summary>The named environment variable is unset or holds an empty value.</summary>
    EnvironmentVariableUnavailable,
}
