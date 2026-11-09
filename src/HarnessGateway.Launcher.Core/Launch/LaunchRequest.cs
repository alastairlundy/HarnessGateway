namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Declarative launch submission: what the caller asked for, before any
/// resolution. Provider lookup, endpoint/protocol pairing, key resolution, and
/// the working directory all derive downstream from these fields —
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
public sealed record LaunchRequest(
    string Harness,
    string? ProviderName,
    string? Endpoint,
    string? Protocol,
    string? Model,
    IReadOnlyCollection<string> Persist,
    string? SessionKey,
    bool ConfirmRunning = false);
