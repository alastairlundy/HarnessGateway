namespace HarnessGateway.Launcher.Core.Backends;

/// <summary>
/// Typed failure classes for backend resolution. Callers map these to their
/// own exit codes; the resolver itself never exits.
/// </summary>
public enum BackendResolutionFailure
{
    None = 0,

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
    InvalidProtocol
}