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
    NoProvider,

    /// <summary>--backend named no saved backend entry.</summary>
    UnknownProviderName,

    /// <summary>The DefaultProvider pointer names no saved backend entry.</summary>
    DanglingDefaultProvider,

    /// <summary>The DefaultProvider pointer is empty while entries exist (no first-entry fallback).</summary>
    AbsentDefaultProvider,

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

    /// <summary>
    /// A GUI harness's app instance is already running and the confirmation
    /// prompt was declined - or no interactive terminal could answer it.
    /// </summary>
    RunningInstanceUnconfirmed,

    /// <summary>
    /// <c>--confirm-running</c> was requested for a Kind=Cli harness, which
    /// has no running instance to confirm.
    /// </summary>
    ConfirmUnsupportedForHarness,

    /// <summary>
    /// The GUI harness's declared configuration surfaces cannot carry what
    /// this launch asked to deliver: a backend or model override with no
    /// capable surface, or a key when only file surfaces are declared.
    /// </summary>
    GuiProviderUnsupported,
}
