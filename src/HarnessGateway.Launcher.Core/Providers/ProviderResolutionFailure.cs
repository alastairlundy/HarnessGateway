namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Typed failure classes for backend resolution. Callers map these to their
/// own exit codes; the resolver itself never exits.
/// </summary>
public enum ProviderResolutionFailure
{
    None = 0,

    /// <summary>--backend named no saved backend entry.</summary>
    UnknownProviderName,

    /// <summary>The default-backend pointer is set but names no saved entry.</summary>
    DanglingDefaultProvider,

    /// <summary>The pointer is empty while saved entries exist — no first-entry fallback.</summary>
    AbsentDefaultProvider,

    /// <summary>--protocol was given without --endpoint.</summary>
    ProtocolWithoutEndpoint,

    /// <summary>--endpoint was given without --protocol.</summary>
    EndpointWithoutProtocol,

    /// <summary>--endpoint is not an absolute URL.</summary>
    InvalidEndpoint,

    /// <summary>--protocol is not one of the known protocol words.</summary>
    InvalidProtocol,
}