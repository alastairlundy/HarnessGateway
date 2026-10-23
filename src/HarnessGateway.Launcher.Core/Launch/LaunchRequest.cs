namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Declarative launch submission: what the caller asked for, before any
/// resolution. Backend lookup, endpoint/protocol pairing, key resolution, and
/// the working directory all derive downstream from these seven fields —
/// <c>SessionKey</c> (the wizard-entered key override) is the only
/// secret-adjacent field, and nothing here carries resolved material.
/// </summary>
public sealed record LaunchRequest(
    string Harness,
    string? BackendName,
    string? Endpoint,
    string? Protocol,
    string? Model,
    IReadOnlyCollection<string> Persist,
    string? SessionKey);
