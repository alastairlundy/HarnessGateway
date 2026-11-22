namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// The single sourcing rule for a provider credential at save time, shared by
/// every credential verb: exactly one of the inline <c>--key</c> value, the
/// variable named by <c>--key-env</c> (read from the launcher process's
/// environment at invocation time - the launcher never mutates its own
/// environment), or neither, in which case the caller prompts. Conflicting
/// flags and an unset or empty variable are typed failures, never a silent
/// fallback to the prompt; a resolved secret flows to the credential store
/// while the settings file keeps only the opaque reference.
/// </summary>
public sealed class ApiKeySource
{
    private readonly Func<string, string?> _readEnvironmentVariable;

    /// <param name="readEnvironmentVariable">
    /// Read seam over the process environment; defaults to
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>. Tests inject
    /// a scripted reader so no test ever mutates the real environment.
    /// </param>
    public ApiKeySource(Func<string, string?>? readEnvironmentVariable = null)
    {
        _readEnvironmentVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>
    /// Resolves the key source. The two flags together fail before the
    /// environment is touched; otherwise the named variable must hold a
    /// non-empty value (unset and empty are the same failure class), the
    /// inline key wins when given alone, and with neither flag the caller
    /// prompts.
    /// </summary>
    public ApiKeySourceResult Resolve(string? key, string? environmentVariableName)
    {
        if (key is not null && environmentVariableName is not null)
        {
            return new ApiKeySourceResult.Failed(
                ApiKeySourceFailure.FlagConflict,
                "'--key' and '--key-env' cannot be used together; pass exactly one key source.");
        }

        if (environmentVariableName is not null)
        {
            // An empty variable name is treated like an unreadable variable
            // rather than handed to the platform API, which may reject it.
            string? value = environmentVariableName.Length == 0
                ? null
                : _readEnvironmentVariable(environmentVariableName);

            if (string.IsNullOrEmpty(value))
            {
                return new ApiKeySourceResult.Failed(
                    ApiKeySourceFailure.EnvironmentVariableUnavailable,
                    $"Environment variable '{environmentVariableName}' is not set or is empty. " +
                    "Set it to a non-empty value before running this command.");
            }

            return new ApiKeySourceResult.Resolved(value);
        }

        return key is not null
            ? new ApiKeySourceResult.Resolved(key)
            : new ApiKeySourceResult.Prompt();
    }
}
