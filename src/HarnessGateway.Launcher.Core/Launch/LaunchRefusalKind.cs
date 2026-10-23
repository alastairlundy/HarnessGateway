namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Typed refusal classes: the user-rectifiable reasons a launch never starts.
/// No kind exists without a producer - exit codes 5 and 6 remain unwritten.
/// </summary>
public enum LaunchRefusalKind
{
    /// <summary>No adapter matched the requested harness token.</summary>
    UnknownHarness,

    /// <summary>The harness is known but not installed or not detectable.</summary>
    HarnessUnavailable,

    /// <summary>No backend could be used: empty settings and no complete endpoint/protocol override.</summary>
    NoBackend,

    /// <summary>--backend named no saved backend entry.</summary>
    UnknownBackendName,

    /// <summary>The DefaultBackend pointer names no saved backend entry.</summary>
    DanglingDefaultBackend,

    /// <summary>The DefaultBackend pointer is empty while entries exist (no first-entry fallback).</summary>
    AbsentDefaultBackend,

    /// <summary>--protocol was given without --endpoint.</summary>
    ProtocolWithoutEndpoint,

    /// <summary>--endpoint was given without --protocol.</summary>
    EndpointWithoutProtocol,

    /// <summary>--endpoint is not an absolute URL.</summary>
    InvalidEndpoint,

    /// <summary>--protocol is not one of the known protocol words.</summary>
    InvalidProtocol,

    /// <summary>The resolved backend's protocol is not supported by the chosen harness.</summary>
    ProtocolNotSupported,

    /// <summary>The Codex provider block collided with the configured backend.</summary>
    ProviderBlockConflict,
}
