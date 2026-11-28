using HarnessGateway.Launcher.Core.Providers.Models;
using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Tests.Providers;

/// <summary>
/// The qualification match spectrum - unique, ambiguous, zero, and
/// already-qualified inputs - resolved against a small in-memory catalog
/// fixture, so the suite never reaches the network or the cache file.
/// </summary>
public class ModelQualifierTests
{
    private static AICatalog Catalog(params (string ProviderId, string[] ModelIds)[] providers)
    {
        AICatalog catalog = new();

        foreach ((string providerId, string[] modelIds) in providers)
        {
            AIModelInfo[] models = [.. modelIds.Select(id => new AIModelInfo { Id = id, Name = id })];
            catalog.Providers[providerId] = new AIProviderInfo
            {
                Id = providerId,
                Name = providerId,
                Models = models,
            };
        }

        return catalog;
    }

    [Test]
    public async Task Unique_catalog_match_auto_qualifies()
    {
        AICatalog catalog = Catalog(
            ("anthropic", ["claude-sonnet-4-5"]),
            ("openai", ["gpt-4o", "gpt-4o-mini"]));

        ModelQualification result = new ModelQualifier().Qualify("gpt-4o-mini", catalog);

        ModelQualification.Qualified qualified = (ModelQualification.Qualified)result;
        await Assert.That(qualified.Reference).IsEqualTo("openai/gpt-4o-mini");
    }

    [Test]
    public async Task Unique_match_is_case_insensitive_and_reports_the_catalog_identifiers()
    {
        AICatalog catalog = Catalog(("anthropic", ["claude-sonnet-4-5"]));

        ModelQualification result = new ModelQualifier().Qualify("CLAUDE-SONNET-4-5", catalog);

        ModelQualification.Qualified qualified = (ModelQualification.Qualified)result;
        await Assert.That(qualified.Reference).IsEqualTo("anthropic/claude-sonnet-4-5");
    }

    [Test]
    public async Task Multiple_catalog_matches_are_ambiguous_in_a_stable_order()
    {
        AICatalog catalog = Catalog(
            ("groq", ["deepseek-r1", "llama-3.3-70b"]),
            ("deepseek", ["deepseek-r1"]));

        ModelQualification result = new ModelQualifier().Qualify("deepseek-r1", catalog);

        ModelQualification.Ambiguous ambiguous = (ModelQualification.Ambiguous)result;
        await Assert.That(ambiguous.Candidates.Count).IsEqualTo(2);
        await Assert.That(ambiguous.Candidates[0]).IsEqualTo("deepseek/deepseek-r1");
        await Assert.That(ambiguous.Candidates[1]).IsEqualTo("groq/deepseek-r1");
    }

    [Test]
    public async Task Zero_catalog_matches_pass_through_with_a_catalog_limitation_warning()
    {
        AICatalog catalog = Catalog(("openai", ["gpt-4o"]));

        ModelQualification result = new ModelQualifier().Qualify("my-local-model", catalog);

        ModelQualification.Unqualified unqualified = (ModelQualification.Unqualified)result;
        await Assert.That(unqualified.BareName).IsEqualTo("my-local-model");

        // The warning names the catalog as the limitation and carries the
        // local-model caveat, so a custom model ID is never read as a typo.
        await Assert.That(unqualified.Warning.Contains(
            "models.dev catalog", StringComparison.Ordinal)).IsTrue();
        await Assert.That(unqualified.Warning.Contains(
            "local models do not appear in the catalog", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Provider_qualified_input_passes_through_verbatim()
    {
        AICatalog catalog = Catalog(("anthropic", ["claude-sonnet-4-5"]));

        ModelQualification result = new ModelQualifier().Qualify("anthropic/claude-sonnet-4-5", catalog);

        ModelQualification.Qualified qualified = (ModelQualification.Qualified)result;
        await Assert.That(qualified.Reference).IsEqualTo("anthropic/claude-sonnet-4-5");
    }

    [Test]
    public async Task Provider_qualified_input_passes_through_verbatim_without_a_catalog()
    {
        ModelQualification result = new ModelQualifier().Qualify("my-provider/my-model", catalog: null);

        ModelQualification.Qualified qualified = (ModelQualification.Qualified)result;
        await Assert.That(qualified.Reference).IsEqualTo("my-provider/my-model");
    }

    [Test]
    public async Task Missing_catalog_passes_the_bare_name_through_with_the_degraded_warning()
    {
        ModelQualification result = new ModelQualifier().Qualify("gpt-4o", catalog: null);

        ModelQualification.Unqualified unqualified = (ModelQualification.Unqualified)result;
        await Assert.That(unqualified.BareName).IsEqualTo("gpt-4o");
        await Assert.That(unqualified.Warning).IsEqualTo(CatalogCache.DegradedWarning);
    }
}
