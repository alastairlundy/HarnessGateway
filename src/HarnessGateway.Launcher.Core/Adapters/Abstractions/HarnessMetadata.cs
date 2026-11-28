namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

/// <summary>
/// Per-harness facts the launcher needs before it can plan a launch.
/// </summary>
/// <param name="Name">
/// Display label. The wizard lists this and error prose quotes it, so it should
/// read the way the vendor writes the product's name ("GitHub Copilot CLI").
/// Not an input token: see <paramref name="CommandName"/>.
/// </param>
/// <param name="CommandName">
/// The token the user types after <c>launch</c> ("copilot"). This is the only
/// form a bare <c>launch &lt;agent&gt;</c> accepts, and the form the wizard
/// returns to the orchestrator. Must be unique across the shipped adapters.
/// </param>
/// <param name="Aliases">
/// Extra accepted tokens, case-insensitive. Empty when
/// <paramref name="CommandName"/> is the only spelling in use. Aliases widen
/// what resolves; they never appear in help output or in the wizard list.
/// </param>
/// <param name="Kind">Whether the harness is a CLI or a GUI app.</param>
/// <param name="RequiresModel">
/// True when the harness will not start usefully without a model. Drives a
/// warning, not a refusal.
/// </param>
/// <param name="InstallHint">Full install instructions line, quoted verbatim in refusals.</param>
/// <param name="DetectionCommand">
/// The command a user can run to check detection by hand. Surfaced in the
/// not-installed refusal so a PATH false negative is diagnosable without
/// reading the source.
/// </param>
/// <param name="ConfigFileCandidates">Harness config files the launcher may touch.</param>
/// <param name="SupportedProtocols">Protocols this harness can speak.</param>
/// <param name="SupportedSurfaces">
/// Configuration mechanisms this harness can actually be configured through.
/// Set semantics like <paramref name="SupportedProtocols"/>: membership, not
/// priority. Kind=Cli adapters pass an empty set - the CLI launch path configures
/// the way it always has, and only GUI planning reads this.
/// </param>
/// <param name="InstanceProcessNames">
/// Process executable names an already-running instance of this harness
/// reports, ordered like <paramref name="Aliases"/>. The order is an authoring
/// convention only - matching never reads it - and availability stays a
/// runtime discovery concern, so Kind=Cli adapters pass an empty list.
/// </param>
/// <param name="ResolvesModelsInOwnCatalog">
/// True when the harness resolves model references in its own
/// multi-provider catalog, so the launch flow qualifies bare model
/// names against the models.dev catalog before handoff. Harnesses whose
/// launcher-side adapter synthesizes the <c>providerId/model</c>
/// reference itself (OpenCode, Kilo) stay false - qualifying for them
/// would double-qualify the reference they build.
/// </param>
public sealed record HarnessMetadata(
    string Name,
    string CommandName,
    IReadOnlyList<string> Aliases,
    HarnessKind Kind,
    bool RequiresModel,
    string InstallHint,
    string DetectionCommand,
    IReadOnlyList<string> ConfigFileCandidates,
    IReadOnlySet<ProtocolKind> SupportedProtocols,
    IReadOnlySet<ConfigurationSurface> SupportedSurfaces,
    IReadOnlyList<string> InstanceProcessNames,
    bool ResolvesModelsInOwnCatalog = false)
{
    /// <summary>Every token that resolves this harness: the command name plus aliases.</summary>
    public IEnumerable<string> AcceptedTokens
    {
        get
        {
            yield return CommandName;
            foreach (string alias in Aliases)
            {
                yield return alias;
            }
        }
    }
}
