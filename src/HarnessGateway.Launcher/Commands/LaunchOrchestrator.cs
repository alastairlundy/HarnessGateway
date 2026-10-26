using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Commands;

/// <summary>
/// The CLI-side adapter over <see cref="LaunchFlow"/>: it turns parsed launch
/// arguments into a declarative <see cref="LaunchRequest"/>, submits it to the
/// flow, and maps the closed <see cref="LaunchOutcome"/> family back to this
/// CLI's exit codes and stderr lines. The launch order itself lives Core-side;
/// this class owns none of it.
/// </summary>
/// <remarks>
/// The wizard handoff is the second caller: it builds its own
/// <see cref="LaunchRequest"/> and shares the same flow instance and the same
/// <see cref="MapOutcomeAsync"/> switch, so no second outcome-mapping
/// convention can form.
/// </remarks>
public sealed class LaunchOrchestrator
{
    private readonly LaunchFlow _flow;

    public LaunchOrchestrator(LaunchFlow flow)
    {
        _flow = flow;
    }

    /// <summary>
    /// The direct CLI path: parsed flags and nothing more. The direct path
    /// never carries a launch-time key - keys are a stored-entry concern here
    /// (set via <c>config backend set-key</c>) - so the request always builds
    /// with a null <see cref="LaunchRequest.SessionKey"/>. The
    /// <paramref name="confirmRunning"/> fact rides verbatim from the flag: the
    /// request builder never inspects the harness kind, because the misuse
    /// refusal (flag against a Kind=Cli harness) belongs to the flow.
    /// </summary>
    public async Task<int> RunAsync(
        string agentName, string? backendName,
        string? endpoint, string? protocol,
        string? model, IReadOnlyList<string> persistValues,
        bool confirmRunning = false,
        CancellationToken cancellationToken = default)
    {
        LaunchRequest request = new(
            agentName,
            backendName,
            endpoint,
            protocol,
            model,
            persistValues,
            SessionKey: null,
            ConfirmRunning: confirmRunning);

        LaunchOutcome outcome = await _flow.ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(true);
        return await MapOutcomeAsync(outcome).ConfigureAwait(true);
    }

    /// <summary>
    /// The one exhaustive outcome-to-exit-code mapping: the flow
    /// speaks outcomes carrying no numbers; this switch is where every kind
    /// picks its CLI exit code and stderr line. Contract values are the
    /// append-only README table and must stay byte-for-byte stable.
    /// </summary>
    public static async Task<int> MapOutcomeAsync(LaunchOutcome outcome)
    {
        switch (outcome)
        {
            case LaunchCompleted completed:
                // The payload is mode-honest: a blocking run's exit status
                // passes through untouched, and a detached start reports the
                // handoff it already succeeded at as Ok - there is no exit
                // code to wait for.
                return completed.Payload switch
                {
                    CompletedPayload.ExitStatus exitStatus => exitStatus.Value,
                    CompletedPayload.HandedOff => (int)LaunchExitCode.Ok,
                    _ => throw new InvalidOperationException(
                        $"unexpected completed payload variant '{completed.Payload?.GetType().Name}'"),
                };

            case LaunchRefused refused:
                // User-rectifiable: the message the flow composed Core-side
                // is printed as-is (never rebuilt here), and the kind picks
                // the code. The "Launch failed:" prefix is never usable by a
                // refusal.
                await Console.Error.WriteLineAsync(refused.Message).ConfigureAwait(true);
                return ExitCodeFor(refused.Kind);

            case LaunchFailed failed:
                // The "Launch failed:" prefix is reserved for the unexpected
                // fault path (the flow's terminal fold, which includes a
                // restore fault in the finally); every other failure keeps
                // the raw message.
                string message = failed.Kind == LaunchFailureKind.Unexpected
                    ? $"Launch failed: {failed.Message}"
                    : failed.Message;
                await Console.Error.WriteLineAsync(message).ConfigureAwait(true);
                return failed.Kind == LaunchFailureKind.UnsupportedSettingsSchema
                    ? (int)LaunchExitCode.UnsupportedSettingsSchema
                    : (int)LaunchExitCode.ConfigRestoreFailure;

            default:
                throw new InvalidOperationException(
                    $"unexpected launch outcome variant '{outcome?.GetType().Name}'");
        }
    }

    /// <summary>
    /// Exhaustive over the twelve refusal kinds: every kind names its
    /// exit code explicitly so a newly added kind cannot silently land on a
    /// catch-all default. Codes are today's values, preserved byte-for-byte.
    /// </summary>
    private static int ExitCodeFor(LaunchRefusalKind kind) => kind switch
    {
        LaunchRefusalKind.NoBackend => (int)LaunchExitCode.UsageError,
        LaunchRefusalKind.UnknownHarness => (int)LaunchExitCode.UnknownAgent,
        LaunchRefusalKind.HarnessUnavailable => (int)LaunchExitCode.HarnessUnavailable,
        LaunchRefusalKind.ProtocolNotSupported => (int)LaunchExitCode.UnknownAgent,
        LaunchRefusalKind.ProviderBlockConflict => (int)LaunchExitCode.ConfigConflict,
        LaunchRefusalKind.UnknownBackendName
            or LaunchRefusalKind.DanglingDefaultBackend
            or LaunchRefusalKind.AbsentDefaultBackend => (int)LaunchExitCode.UnknownBackendName,
        // Pair-rule and invalid-override refusals keep the resolution
        // failure's historical default: usage error.
        LaunchRefusalKind.ProtocolWithoutEndpoint
            or LaunchRefusalKind.EndpointWithoutProtocol
            or LaunchRefusalKind.InvalidEndpoint
            or LaunchRefusalKind.InvalidProtocol => (int)LaunchExitCode.UsageError,
        // Every defined kind is named above; a value outside the enum can
        // only arrive from a cast of garbage, which is a wiring bug, not a
        // user-facing exit code.
        _ => throw new InvalidOperationException(
            $"unexpected launch refusal kind '{kind}'"),
    };
}
