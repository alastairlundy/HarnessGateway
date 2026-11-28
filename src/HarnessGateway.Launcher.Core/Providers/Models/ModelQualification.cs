namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// Outcome of translating a model reference against the models.dev
/// catalog at the launch handoff: a closed family of exactly three
/// sealed variants, one per branch of the match spectrum, so the
/// consumers that turn ambiguity into an interactive pick or a clear
/// refusal are exhaustive at the type level instead of letting a
/// confused reference reach the harness and fail there.
/// </summary>
public abstract record ModelQualification
{
    /// <summary>
    /// The bare name matched more than one harness-side provider. The
    /// payload is the ordered provider-qualified candidate list, each
    /// entry in the <c>provider/model</c> shape the harness expects: the
    /// interactive picker offers these entries and the non-interactive
    /// refusal lists them. The order is deterministic (ordinal by
    /// candidate), so the picker and any test see one stable list
    /// whatever the catalog's dictionary iteration order does.
    /// </summary>
    public sealed record Ambiguous(IReadOnlyList<string> Candidates) : ModelQualification;

    /// <summary>
    /// The harness-expected <c>provider/model</c> reference to hand to
    /// the adapter: the catalog's unique match for a bare name, or the
    /// input itself when it was already provider-qualified and therefore
    /// passed through verbatim.
    /// </summary>
    public sealed record Qualified(string Reference) : ModelQualification;

    /// <summary>
    /// The bare name could not be qualified, so it passes through
    /// unchanged next to the warning to print with it: the zero-match
    /// text that points at the catalog as the limitation and carries the
    /// local-model caveat, or - when this launch has no catalog at all -
    /// the cache's degraded-disambiguation warning, which explains the
    /// missing authority instead of claiming a match against nothing.
    /// </summary>
    public sealed record Unqualified(string BareName, string Warning) : ModelQualification;
}
