/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

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
    /// (set via <c>provider set-key</c>) - so the request always builds
    /// with a null <see cref="LaunchRequest.SessionKey"/>. The
    /// <paramref name="confirmRunning"/> fact is passed verbatim from the flag: the
    /// request builder never inspects the harness kind, because the misuse
    /// refusal (flag against a Kind=Cli harness) belongs to the flow.
    /// <paramref name="print"/> and <paramref name="printPrompt"/> are passed the
    /// same way - whether the harness has a one-shot mode or a prompt
    /// arrived are the flow's refusals, never this mapping layer's.
    /// </summary>
    public async Task<int> RunAsync(
        string agentName, string? providerName,
        string? endpoint, string? protocol,
        string? model, IReadOnlyList<string> persistValues,
        bool confirmRunning = false,
        bool print = false,
        string? printPrompt = null,
        CancellationToken cancellationToken = default)
    {
        LaunchRequest request = new(
            agentName,
            providerName,
            endpoint,
            protocol,
            model,
            persistValues,
            SessionKey: null,
            ConfirmRunning: confirmRunning,
            Print: print,
            PrintPrompt: printPrompt);

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
                // The payload matches the mode: a blocking run's exit status
                // passes through untouched, and a detached start reports the
                // handoff it already succeeded at as Ok - there is no exit
                // code to wait for.
                return completed.Payload switch
                {
                    CompletedPayload.ExitStatus exitStatus => exitStatus.Value,
                    CompletedPayload.HandedOff => (int)LaunchExitCode.Ok,
                    _ => throw new InvalidOperationException(
                        $"unexpected completed payload variant '{completed.Payload.GetType().Name}'"),
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
                    $"unexpected launch outcome variant '{outcome.GetType().Name}'");
        }
    }

    /// <summary>
    /// Exhaustive over the nineteen refusal kinds: every kind names its
    /// exit code explicitly so a newly added kind cannot silently land on a
    /// catch-all default. Codes are today's values, preserved byte-for-byte;
    /// the three GUI kinds are producer additions to existing families, the
    /// unlisted-model kind reuses the UnknownAgent number, and the two print
    /// misuse kinds reuse UsageError - TerminalRequired is the only new
    /// number (13).
    /// </summary>
    private static int ExitCodeFor(LaunchRefusalKind kind) => kind switch
    {
        LaunchRefusalKind.NoProvider => (int)LaunchExitCode.UsageError,
        LaunchRefusalKind.UnknownHarness => (int)LaunchExitCode.UnknownAgent,
        LaunchRefusalKind.HarnessUnavailable => (int)LaunchExitCode.HarnessUnavailable,
        LaunchRefusalKind.ProtocolNotSupported => (int)LaunchExitCode.UnknownAgent,
        // The unlisted-model refusal reuses exit 3: no new exit code, the
        // README table keeps its size.
        LaunchRefusalKind.UnlistedModel => (int)LaunchExitCode.UnknownAgent,
        LaunchRefusalKind.ProviderBlockConflict => (int)LaunchExitCode.ConfigConflict,
        // GUI stops reuse the unavailable-harness family: a confirmation
        // nothing confirmed and a surface nothing can carry both mean this
        // launch cannot run as asked.
        LaunchRefusalKind.RunningInstanceUnconfirmed
            or LaunchRefusalKind.GuiProviderUnsupported => (int)LaunchExitCode.HarnessUnavailable,
        // Flag misuse is a usage error, same as every other bad-flag stop:
        // --confirm-running against a CLI harness, and the two -p/--print
        // misuses (no one-shot mode, no prompt).
        LaunchRefusalKind.ConfirmUnsupportedForHarness
            or LaunchRefusalKind.PrintUnsupportedForHarness
            or LaunchRefusalKind.PrintPromptMissing => (int)LaunchExitCode.UsageError,
        // An interactive launch with no terminal to run in is an appended
        // code of its own: the launch is correctly requested but cannot run
        // in this context, which a usage error would misreport as a bad
        // flag and an unavailable harness would misreport as a bad install.
        LaunchRefusalKind.TerminalRequired => (int)LaunchExitCode.TerminalRequired,
        LaunchRefusalKind.UnknownProviderName
            or LaunchRefusalKind.DanglingDefaultProvider
            or LaunchRefusalKind.AbsentDefaultProvider => (int)LaunchExitCode.UnknownProviderName,
        // Pair-rule and invalid-override refusals keep the resolution
        // failure's default: usage error.
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
