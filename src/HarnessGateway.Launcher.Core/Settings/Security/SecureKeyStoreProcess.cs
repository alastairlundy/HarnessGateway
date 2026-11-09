using CliInvoke;
using CliInvoke.Builders;

namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Shared CliInvoke plumbing for the two process-backed key stores. The only
/// place a store passes a secret to a child process - always on stdin, never
/// on argv.
/// </summary>
internal static class SecureKeyStoreProcess
{
    /// <summary>
    /// Runs <paramref name="targetFilePath"/> and returns its buffered result.
    /// A non-zero exit code is a normal result (the callers interpret it); a
    /// failure to launch the process at all becomes an instructive
    /// <see cref="SecureKeyStoreException"/> built from <paramref name="failureDetail"/>.
    /// </summary>
    internal static async Task<BufferedProcessResult> RunAsync(
        string targetFilePath,
        IReadOnlyList<string> arguments,
        string providerName,
        string operation,
        string failureDetail,
        StreamWriter? standardInput,
        CancellationToken cancellationToken)
    {
        try
        {
            using ProcessConfigurationBuilder builder = new(targetFilePath);
            builder.SetArgumentList(arguments);
            builder.SetOutputRedirection(true);

            if (standardInput is not null)
            {
                builder.RedirectStandardInput(true);
                builder.SetStandardInputPipe(standardInput);
            }

            return await CliRun.RunBufferedAsync(builder.Build(), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw SecureKeyStoreException.OperationFailed(providerName, operation, failureDetail, ex);
        }
    }

    /// <summary>
    /// Interprets a <c>secret-tool clear</c> exit code for the idempotent
    /// delete contract: the Secret Service <c>Clear</c> call succeeds when
    /// nothing matched, so a non-zero exit can only be an operational
    /// failure - a locked collection, an unreachable session - and must be
    /// reported instead of swallowed.
    /// </summary>
    internal static bool LinuxDeleteFailed(int exitCode) => exitCode != 0;

    /// <summary>
    /// Interprets a <c>security delete-generic-password</c> result: both the
    /// idempotent miss and an operational failure exit non-zero, so only the
    /// recognized "could not be found" miss succeeds. Ambiguity fails loud -
    /// a miss whose wording does not match reports a failure rather than
    /// silently orphaning a stored secret.
    /// </summary>
    internal static bool MacOsDeleteFailed(int exitCode, string? standardError) =>
        exitCode != 0
        && (standardError is null
            || !standardError.Contains("could not be found", StringComparison.OrdinalIgnoreCase));
}
