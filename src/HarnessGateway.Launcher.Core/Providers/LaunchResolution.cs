namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// The sealed two-case launch family: what a launch resolved to, after
/// precedence and override layering. A <see cref="ProviderCase"/> carries the
/// whole stored <see cref="Providers.Provider"/> identity with its effective
/// model and protocol; an <see cref="AdHocCase"/> carries a raw-URL launch
/// with no saved provider behind it - keyless by design, so the type system
/// forbids a keyless launch from reaching consumers that assume a stored
/// provider exists. Both cases expose <c>(Protocol, BaseUri)</c> for the
/// emission planner's contract. Failure shapes stay on
/// <see cref="ProviderResolutionResult"/>, outside this family.
/// </summary>
public abstract record LaunchResolution
{
    /// <summary>The session's effective base URI.</summary>
    public abstract Uri BaseUri { get; }

    /// <summary>The session's effective wire protocol.</summary>
    public abstract ProtocolKind Protocol { get; }

    /// <summary>
    /// A stored provider resolved for launch: the whole session-layered
    /// <see cref="Providers.Provider"/> - endpoint and default protocol
    /// replaced wholesale when the flag pair is present, the listed model
    /// entry's protocol override otherwise - plus the effective model and
    /// effective protocol after precedence. The provider's key reference is
    /// the sole credential source; keys are never overridable per launch.
    /// </summary>
    public sealed record ProviderCase : LaunchResolution
    {
        public ProviderCase(Provider provider, string? model, ProtocolKind protocol)
        {
            Provider = provider;
            Model = model;
            Protocol = protocol;
        }

        /// <summary>The stored provider identity, layered for this session.</summary>
        public Provider Provider { get; }

        /// <summary>The effective model: <c>--model</c>, else the provider's default model.</summary>
        public string? Model { get; }

        /// <inheritdoc />
        public override ProtocolKind Protocol { get; }

        /// <inheritdoc />
        public override Uri BaseUri => Provider.BaseUri;
    }

    /// <summary>
    /// A raw-URL launch with no saved provider: the endpoint and protocol
    /// come from the flag pair alone, and there is no key reference of any
    /// kind - raw-URL launches stay keyless by design.
    /// </summary>
    public sealed record AdHocCase : LaunchResolution
    {
        public AdHocCase(Uri baseUri, ProtocolKind protocol, string? model)
        {
            BaseUri = baseUri;
            Protocol = protocol;
            Model = model;
        }

        /// <inheritdoc />
        public override Uri BaseUri { get; }

        /// <inheritdoc />
        public override ProtocolKind Protocol { get; }

        /// <summary>The <c>--model</c> flag for this session, when given.</summary>
        public string? Model { get; }
    }
}