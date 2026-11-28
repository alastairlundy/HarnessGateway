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

    /// <summary>No provider could be used: empty settings and no complete endpoint/protocol override.</summary>
    NoProvider,

    /// <summary>--provider named no saved provider entry.</summary>
    UnknownProviderName,

    /// <summary>The DefaultProvider pointer names no saved provider entry.</summary>
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
    /// this launch asked to deliver: a provider or model override with no
    /// capable surface, or a key when only file surfaces are declared.
    /// </summary>
    GuiProviderUnsupported,

    /// <summary>
    /// The effective model names no entry in the provider's non-empty models
    /// array (or a DefaultModel missing from it) - the user-rectifiable
    /// unlisted-model refusal that reuses exit code 3.
    /// </summary>
    UnlistedModel,

    /// <summary>
    /// An interactive CLI launch with no terminal on stdin or stdout (piped
    /// or redirected): the harness would render into handles nobody can
    /// read. The message points at <c>-p/--print</c> as the one-shot
    /// alternative. Producers exit 13.
    /// </summary>
    TerminalRequired,

    /// <summary>
    /// <c>-p/--print</c> against a harness with no non-interactive mode the
    /// launcher knows how to drive: a GUI application, or a CLI harness
    /// whose one-shot spelling is not implemented. Producers exit 2.
    /// </summary>
    PrintUnsupportedForHarness,

    /// <summary>
    /// <c>-p/--print</c> with no prompt to run: none passed as an argument
    /// and none readable from stdin. Producers exit 2.
    /// </summary>
    PrintPromptMissing,

    /// <summary>
    /// A bare model name matched several harness-side providers in the
    /// models.dev catalog and the session is non-interactive (no terminal,
    /// or <c>--non-interactive</c>): the message lists the
    /// provider-qualified matches and a docs pointer. Producers exit 14.
    /// </summary>
    AmbiguousModel,
}
