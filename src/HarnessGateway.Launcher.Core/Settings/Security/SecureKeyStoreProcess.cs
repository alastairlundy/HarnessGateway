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
        string backendName,
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
            throw SecureKeyStoreException.OperationFailed(backendName, operation, failureDetail, ex);
        }
    }
}
