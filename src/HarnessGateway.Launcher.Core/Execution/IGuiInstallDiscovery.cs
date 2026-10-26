namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// Where a Kind=Gui harness's executable actually is: the launcher asks the
/// operating system at launch time instead of trusting a declared path, so
/// availability stays truthful as apps install, update, or move. This is the
/// seam that supersedes adapter-declared install-path probing (D009): the
/// harness's declared identity strings are the only per-harness input, and the
/// conventional install roots are launcher-owned operating-system facts.
/// </summary>
public interface IGuiInstallDiscovery
{
    /// <summary>
    /// Searches for an executable matching any entry of
    /// <paramref name="instanceProcessNames"/> and reports whether one was
    /// found and where. Resolution is resolver-first (PATH lookup through
    /// CliInvoke's no-throw <c>TryResolveFilePath</c>), then a scan of the
    /// current OS's conventional install roots, both honouring the declared
    /// list's order. Read-only by contract: it starts no processes and writes
    /// nothing to disk, and an exhausted search reports
    /// <c>(Found: false, ExecutablePath: null)</c> instead of throwing - the
    /// caller turns that into its own refusal.
    /// </summary>
    /// <param name="instanceProcessNames">
    /// The harness's declared identity list, from
    /// <c>HarnessMetadata.InstanceProcessNames</c>. Empty input is an
    /// immediate miss.
    /// </param>
    (bool Found, string? ExecutablePath) Discover(IReadOnlyList<string> instanceProcessNames);
}
