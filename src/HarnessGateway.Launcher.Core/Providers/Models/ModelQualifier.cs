using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// Translates a model reference into a <see cref="ModelQualification"/>
/// against the models.dev catalog at the launch handoff. The catalog is
/// the single translation authority for model names: every launched
/// model goes through this resolver - the flag value, a model drawn from
/// the saved provider's pre-configured list, and the provider default
/// alike - so the saved list stays convenience and defaulting rather
/// than a parallel naming authority.
///
/// The match spectrum: a bare name with exactly one catalog match
/// auto-qualifies, a name listed by several harness-side providers comes
/// back <see cref="ModelQualification.Ambiguous"/> for the picker or the
/// refusal to consume, and a name the catalog does not list passes
/// through with a warning that says so. A reference that is already
/// <c>provider/model</c>-shaped is in the harness's own namespace, so it
/// passes through verbatim without being qualified again.
/// </summary>
public sealed class ModelQualifier
{
    /// <summary>
    /// Qualify <paramref name="modelReference"/> against
    /// <paramref name="catalog"/>. A null catalog is the degraded launch
    /// (cold cache or failed fetch): with no authority to translate
    /// against, the bare name passes through carrying
    /// <see cref="CatalogCache.DegradedWarning"/> rather than a
    /// zero-match claim the caller could not actually make.
    /// </summary>
    public ModelQualification Qualify(string modelReference, AICatalog? catalog)
    {
        // Already provider-qualified: the harness resolves it as-is, and
        // re-qualifying would rewrite a reference that is already correct
        // (or, with no catalog, wrongly report it degraded).
        if (modelReference.Contains('/'))
        {
            return new ModelQualification.Qualified(modelReference);
        }

        if (catalog is null)
        {
            return new ModelQualification.Unqualified(modelReference, CatalogCache.DegradedWarning);
        }

        List<string> candidates = MatchCandidates(modelReference, catalog);

        if (candidates.Count == 0)
        {
            return new ModelQualification.Unqualified(modelReference, ZeroMatchWarning(modelReference));
        }

        if (candidates.Count == 1)
        {
            return new ModelQualification.Qualified(candidates[0]);
        }

        return new ModelQualification.Ambiguous(candidates);
    }

    /// <summary>
    /// Every harness-side provider that lists the bare name, as
    /// <c>provider/model</c> references in a deterministic ordinal order.
    /// Matching ignores case so a hand-typed name still qualifies, and the
    /// candidate reports the catalog's own identifiers rather than the
    /// caller's spelling, so the reference handed to the harness is the
    /// canonical one.
    /// </summary>
    private static List<string> MatchCandidates(string bareName, AICatalog catalog)
    {
        List<string> candidates = [];

        foreach ((string providerId, AIProviderInfo provider) in catalog.Providers)
        {
            AIModelInfo? match = provider.Models.FirstOrDefault(model =>
                string.Equals(model.Id, bareName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                candidates.Add($"{providerId}/{match.Id}");
            }
        }

        candidates.Sort(StringComparer.Ordinal);
        return candidates;
    }

    /// <summary>
    /// The zero-match warning: it points at the catalog as the
    /// limitation - this is a thing the launcher's authority could not do
    /// - and carries the local-model caveat, because a model run outside
    /// models.dev is absent from the catalog by construction rather than
    /// misnamed.
    /// </summary>
    private static string ZeroMatchWarning(string bareName) =>
        $"model '{bareName}' is not in the models.dev catalog, so it passes through unqualified. " +
        "The catalog is the limitation: local models do not appear in the catalog.";
}
