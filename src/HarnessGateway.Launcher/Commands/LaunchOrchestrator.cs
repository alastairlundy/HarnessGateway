using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Commands;

/// <summary>
/// CLI-side entry into the launch pipeline. Intermediate shape (ticket 002):
/// the pipeline itself lives in <see cref="LaunchFlow"/>; this class only
/// builds the declarative request and folds the typed outcome back to an exit
/// code. Ticket 003 replaces the fold with the formal exhaustive switch.
/// </summary>
public sealed class LaunchOrchestrator
{
    private readonly LaunchFlow _flow;

    public LaunchOrchestrator(
        SettingsStore settingsStore,
        BackupService backupService,
        ILaunchRunner runner,
        IReadOnlyList<IHarnessAdapter> adapters,
        ISecureKeyStore keyStore)
    {
        _flow = new LaunchFlow(settingsStore, backupService, runner, adapters, keyStore);
    }

    public async Task<int> RunAsync(
        string agentName, string? backendName,
        string? endpoint, string? protocol,
        string? model, IReadOnlyList<string> persistValues,
        string workingDirectory, string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        // Intermediate delegation (ticket 003 replaces this fold): the flow
        // owns the pipeline and returns the closed outcome family; this fold
        // maps it straight back to today's exit codes and stderr writes.
        //
        // `workingDirectory` is deliberately unused - per decision I003 the
        // flow captures Environment.CurrentDirectory itself, and both callers
        // passed Directory.GetCurrentDirectory() (the same value), so
        // behavior is unchanged until ticket 003 reshapes the call sites.
        _ = workingDirectory;

        LaunchOutcome outcome = await _flow.ExecuteAsync(
            new LaunchRequest(
                agentName,
                backendName,
                endpoint,
                protocol,
                model,
                persistValues,
                apiKey),
            cancellationToken).ConfigureAwait(true);

        // Non-exhaustive on purpose: the `_` defaults and the catch-all
        // variant are the intermediate fold's shortcuts, to be replaced by
        // the exhaustive switch in ticket 003.
        switch (outcome)
        {
            case LaunchCompleted completed:
                return completed.ExitStatus;

            case LaunchRefused refused:
                // User-rectifiable: print the message the flow composed
                // Core-side and pick the recorded code.
                await Console.Error.WriteLineAsync(refused.Message).ConfigureAwait(true);
                return ExitCodeFor(refused.Kind);

            case LaunchFailed failed:
                // The "Launch failed:" prefix is reserved for the unexpected
                // fault path (today's catch-all), which includes a restore
                // fault in the flow's finally; every other failure keeps the
                // raw message.
                string message = failed.Kind == LaunchFailureKind.Unexpected
                    ? $"Launch failed: {failed.Message}"
                    : failed.Message;
                await Console.Error.WriteLineAsync(message).ConfigureAwait(true);
                return failed.Kind == LaunchFailureKind.UnsupportedSettingsSchema
                    ? (int)LaunchExitCode.UnsupportedSettingsSchema
                    : (int)LaunchExitCode.ConfigRestoreFailure;

            default:
                throw new InvalidOperationException(
                    $"unexpected launch outcome variant '{outcome.GetType().Name}'");
        }
    }

    private static int ExitCodeFor(LaunchRefusalKind kind) => kind switch
    {
        LaunchRefusalKind.UnknownBackendName
            or LaunchRefusalKind.DanglingDefaultBackend
            or LaunchRefusalKind.AbsentDefaultBackend => (int)LaunchExitCode.UnknownBackendName,
        LaunchRefusalKind.UnknownHarness
            or LaunchRefusalKind.ProtocolNotSupported => (int)LaunchExitCode.UnknownAgent,
        LaunchRefusalKind.HarnessUnavailable => (int)LaunchExitCode.HarnessUnavailable,
        LaunchRefusalKind.ProviderBlockConflict => (int)LaunchExitCode.ConfigConflict,
        // Pair-rule and invalid-override refusals keep the resolution
        // failure's historical default: usage error.
        _ => (int)LaunchExitCode.UsageError,
    };
}
