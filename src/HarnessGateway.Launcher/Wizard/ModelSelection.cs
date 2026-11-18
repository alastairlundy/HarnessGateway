namespace HarnessGateway.Launcher.Wizard;

/// <summary>
/// The outcome of one <see cref="ModelStep"/> run: the selected model name (or
/// <c>null</c> when skipped/unavailable) and the effective protocol for that
/// selection (null when the selection carries no protocol finding). The wizard
/// is write-free: model entries are saved only through the
/// <c>provider model</c> verbs, never here.
/// </summary>
public sealed record ModelSelection(
    string? Model,
    ProtocolKind? Protocol);