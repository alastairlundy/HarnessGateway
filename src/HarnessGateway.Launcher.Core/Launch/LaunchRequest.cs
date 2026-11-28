namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Declarative launch submission: what the caller asked for, before any
/// resolution. Provider lookup, endpoint/protocol pairing, key resolution, and
/// the working directory all derive downstream from these fields.
/// <c>SessionKey</c> (the wizard-entered key override) is the only
/// secret-adjacent field, and nothing here carries resolved material.
/// </summary>
/// <param name="ConfirmRunning">
/// The scripted answer to the running-instance confirmation, mirroring the bare
/// <c>--confirm-running</c> flag one-for-one: <c>true</c> asserts the caller has
/// already confirmed the live instance, so the flow skips its prompt.
/// Non-nullable with a <c>false</c> default - there is no "unanswered" state,
/// because an absent flag and a declined prompt are different facts (the
/// decline is a refusal, not a field value). The wizard never sets it: no
/// wizard step can know the running-instance state when it builds the request.
/// </param>
/// <param name="Print">
/// The bare <c>-p</c>/<c>--print</c> flag one-for-one: <c>true</c> asks for a
/// one-shot non-interactive run of the harness instead of an interactive
/// session. Non-nullable with a <c>false</c> default; the wizard never sets
/// it. The flow gates it - a harness with no non-interactive mode refuses,
/// as does a GUI harness.
/// </param>
/// <param name="PrintPrompt">
/// The prompt for a <c>Print</c> launch: the <c>-p</c>/<c>--print</c>
/// argument, or the text read from stdin when the flag carried no value.
/// <c>null</c> when <c>Print</c> is false; whitespace-only content is
/// refused by the flow rather than passed to the harness.
/// </param>
/// <param name="NonInteractive">
/// The bare <c>--non-interactive</c> flag one-for-one: <c>true</c> forces
/// the ambiguous-model refusal-with-guidance regardless of whether stdin
/// is attached to a terminal, so the in-terminal picker never runs.
/// Non-nullable with a <c>false</c> default; the wizard never sets it.
/// </param>
/// <param name="CleanEnv">
/// The bare <c>--clean-env</c> flag one-for-one: <c>true</c> asks for the
/// Environment Variable Scrub - the curated API-key variables are removed
/// from the spawned harness process environment. Absent the flag the child
/// inherits everything, unchanged. The launcher's own environment is never
/// touched either way.
/// </param>
public sealed record LaunchRequest(
    string Harness,
    string? ProviderName,
    string? Endpoint,
    string? Protocol,
    string? Model,
    IReadOnlyCollection<string> Persist,
    string? SessionKey,
    bool ConfirmRunning = false,
    bool Print = false,
    string? PrintPrompt = null,
    bool NonInteractive = false,
    bool CleanEnv = false);
