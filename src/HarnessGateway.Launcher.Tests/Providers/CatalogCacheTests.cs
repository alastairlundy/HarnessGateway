using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelsDotDevSharp;
using ModelsDotDevSharp.Abstractions;
using ModelsDotDevSharp.Extensions;

namespace HarnessGateway.Launcher.Tests.Providers;

/// <summary>
/// Component tests for the models.dev catalog cache: staleness runs
/// through the clock seam and every fetch runs through the models.dev
/// client's transport seam, so the suite never reaches the network and
/// never waits out the real freshness window.
/// </summary>
public class CatalogCacheTests
{
    private static readonly DateTimeOffset Baseline = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // A trimmed copy of the live catalog's shape: two harness-side
    // providers with models under the same keys the API serves (reasoning
    // options included), plus one top-level model-metadata entry.
    private const string CatalogJson = """
        {
          "providers": {
            "anthropic": {
              "id": "anthropic",
              "env": ["ANTHROPIC_API_KEY"],
              "npm": "@ai-sdk/anthropic",
              "api": "https://api.anthropic.com/v1",
              "name": "Anthropic",
              "doc": "https://docs.anthropic.com",
              "models": {
                "claude-sonnet-4-5": {
                  "id": "claude-sonnet-4-5",
                  "name": "Claude Sonnet 4.5",
                  "description": "Fast, capable model",
                  "family": "claude-sonnet-4",
                  "reasoning": true,
                  "reasoning_options": [{ "type": "effort", "values": ["low", "medium"] }],
                  "tool_call": true,
                  "temperature": true,
                  "release_date": "2025-09-29",
                  "modalities": { "input": ["text", "image"], "output": ["text"] },
                  "limit": { "context": 200000, "output": 64000 },
                  "cost": { "input": 3, "output": 15 }
                }
              }
            },
            "openai": {
              "id": "openai",
              "env": ["OPENAI_API_KEY"],
              "npm": "@ai-sdk/openai",
              "api": "https://api.openai.com/v1",
              "name": "OpenAI",
              "doc": "https://platform.openai.com/docs",
              "models": {
                "gpt-4o": {
                  "id": "gpt-4o",
                  "name": "GPT-4o",
                  "family": "gpt-4o",
                  "tool_call": true,
                  "temperature": true,
                  "release_date": "2024-05-13",
                  "modalities": { "input": ["text", "image"], "output": ["text"] },
                  "limit": { "context": 128000, "output": 16384 },
                  "cost": { "input": 2.5, "output": 10 }
                },
                "gpt-4o-mini": {
                  "id": "gpt-4o-mini",
                  "name": "GPT-4o mini",
                  "family": "gpt-4o",
                  "tool_call": true,
                  "temperature": true,
                  "release_date": "2024-07-18",
                  "modalities": { "input": ["text", "image"], "output": ["text"] },
                  "limit": { "context": 128000, "output": 16384 },
                  "cost": { "input": 0.15, "output": 0.6 }
                }
              }
            }
          },
          "models": {
            "anthropic/claude-sonnet-4-5": {
              "id": "anthropic/claude-sonnet-4-5",
              "name": "Claude Sonnet 4.5",
              "family": "claude-sonnet-4",
              "reasoning": true,
              "tool_call": true,
              "temperature": true,
              "release_date": "2025-09-29",
              "modalities": { "input": ["text", "image"], "output": ["text"] },
              "limit": { "context": 200000, "output": 64000 }
            }
          }
        }
        """;

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-catalogcache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static CatalogCache CreateCache(
        string filePath,
        StubHttpMessageHandler handler,
        FakeTimeProvider clock)
    {
        return new CatalogCache(CreateRepository(handler), clock, filePath);
    }

    private static ICatalogRepository CreateRepository(StubHttpMessageHandler handler)
    {
        return new CatalogRepository(
            new StubHttpClientFactory(handler),
            Options.Create(new ModelsDevOptions()));
    }

    /// <summary>
    /// Fetches once into <paramref name="filePath"/> and returns the raw
    /// file text, so a test can assert on exactly what later calls leave
    /// (or do not leave) behind.
    /// </summary>
    private static async Task<string> SeedAsync(string filePath, FakeTimeProvider clock)
    {
        CatalogCacheResult seeded = await CreateCache(
            filePath,
            new StubHttpMessageHandler(CatalogJson),
            clock).GetCatalogAsync();

        await Assert.That(seeded.Catalog).IsNotNull();
        await Assert.That(seeded.DegradedWarning).IsNull();

        return await File.ReadAllTextAsync(filePath);
    }

    [Test]
    public async Task Cold_cache_fetch_saves_the_snapshot_and_reports_the_catalog()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            StubHttpMessageHandler handler = new(CatalogJson);
            CatalogCache cache = CreateCache(filePath, handler, new FakeTimeProvider(Baseline));

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(result.DegradedWarning).IsNull();
            await Assert.That(result.Catalog.Providers.Count).IsEqualTo(2);
            await Assert.That(handler.CallCount).IsEqualTo(1);
            await Assert.That(File.Exists(filePath)).IsTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Fresh_snapshot_is_served_without_a_fetch()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            FakeTimeProvider clock = new(Baseline);
            await SeedAsync(filePath, clock);

            clock.Advance(CatalogCache.FreshnessWindow - TimeSpan.FromSeconds(1));
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache cache = CreateCache(filePath, handler, clock);

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(result.DegradedWarning).IsNull();

            // A fetch would have thrown: no call happened at all.
            await Assert.That(handler.CallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Snapshot_at_exactly_the_window_is_still_fresh()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            FakeTimeProvider clock = new(Baseline);
            await SeedAsync(filePath, clock);

            // "Older than one hour" is stale; exactly one hour is not.
            clock.Advance(CatalogCache.FreshnessWindow);
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache cache = CreateCache(filePath, handler, clock);

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(result.DegradedWarning).IsNull();
            await Assert.That(handler.CallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Snapshot_older_than_the_window_is_refetched()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            FakeTimeProvider clock = new(Baseline);
            await SeedAsync(filePath, clock);

            clock.Advance(CatalogCache.FreshnessWindow + TimeSpan.FromSeconds(1));
            StubHttpMessageHandler handler = new(CatalogJson);
            CatalogCache cache = CreateCache(filePath, handler, clock);

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(result.DegradedWarning).IsNull();
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Fetch_failure_with_a_stale_snapshot_degrades_and_keeps_the_file()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            FakeTimeProvider clock = new(Baseline);
            string snapshotBefore = await SeedAsync(filePath, clock);

            clock.Advance(CatalogCache.FreshnessWindow + TimeSpan.FromSeconds(1));
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache cache = CreateCache(filePath, handler, clock);

            CatalogCacheResult result = await cache.GetCatalogAsync();

            // Degraded: no qualification this launch, warning surfaced.
            await Assert.That(result.Catalog).IsNull();
            await Assert.That(result.DegradedWarning).IsEqualTo(CatalogCache.DegradedWarning);

            // One fetch attempted, and the stale snapshot survives it
            // byte for byte.
            await Assert.That(handler.CallCount).IsEqualTo(1);
            await Assert.That(await File.ReadAllTextAsync(filePath)).IsEqualTo(snapshotBefore);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Cold_cache_fetch_failure_degrades_without_writing_a_file()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache cache = CreateCache(filePath, handler, new FakeTimeProvider(Baseline));

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNull();
            await Assert.That(result.DegradedWarning).IsEqualTo(CatalogCache.DegradedWarning);
            await Assert.That(handler.CallCount).IsEqualTo(1);
            await Assert.That(File.Exists(filePath)).IsFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Failed_fetch_is_attempted_only_once_per_launch()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache cache = CreateCache(filePath, handler, new FakeTimeProvider(Baseline));

            CatalogCacheResult first = await cache.GetCatalogAsync();
            CatalogCacheResult second = await cache.GetCatalogAsync();

            await Assert.That(first.Catalog).IsNull();
            await Assert.That(second.Catalog).IsNull();
            await Assert.That(second.DegradedWarning).IsEqualTo(CatalogCache.DegradedWarning);
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Successful_fetch_is_reused_by_later_calls_in_the_same_launch()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            StubHttpMessageHandler handler = new(CatalogJson);
            CatalogCache cache = CreateCache(filePath, handler, new FakeTimeProvider(Baseline));

            CatalogCacheResult first = await cache.GetCatalogAsync();
            CatalogCacheResult second = await cache.GetCatalogAsync();

            await Assert.That(first.Catalog).IsNotNull();
            await Assert.That(second.Catalog).IsNotNull();
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Load_save_round_trip_preserves_the_catalog()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            FakeTimeProvider clock = new(Baseline);
            await SeedAsync(filePath, clock);

            // A fresh snapshot reads back from disk without touching the
            // transport, so what follows is purely the round trip.
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            CatalogCache reloaded = CreateCache(filePath, handler, clock);

            CatalogCacheResult result = await reloaded.GetCatalogAsync();

            await Assert.That(handler.CallCount).IsEqualTo(0);
            await Assert.That(result.Catalog).IsNotNull();
            AICatalog catalog = result.Catalog!;

            await Assert.That(catalog.Providers.Count).IsEqualTo(2);
            await Assert.That(catalog.Providers["anthropic"].Name).IsEqualTo("Anthropic");
            await Assert.That(catalog.Providers["anthropic"].Models?.Length).IsEqualTo(1);
            await Assert.That(catalog.Providers["openai"].Models?.Length).IsEqualTo(2);

            AIModelInfo? model = catalog.Providers["anthropic"].Models?.FirstOrDefault();
            await Assert.That(model).IsNotNull();
            await Assert.That(model!.Id).IsEqualTo("claude-sonnet-4-5");
            await Assert.That(model.SupportsReasoning).IsTrue();
            AIModelReasoningOption? reasoningOption = model.ReasoningOptions?.FirstOrDefault();
            await Assert.That(reasoningOption).IsNotNull();
            await Assert.That(reasoningOption!.Type).IsEqualTo(AIModelReasoningOptionType.Effort);

            await Assert.That(catalog.Models.Count).IsEqualTo(1);
            await Assert.That(catalog.Models.ContainsKey("anthropic/claude-sonnet-4-5")).IsTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Corrupt_snapshot_is_treated_as_cold_and_refetched()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "models-dev-catalog.json");
            await File.WriteAllTextAsync(filePath, "{ not json");

            StubHttpMessageHandler handler = new(CatalogJson);
            CatalogCache cache = CreateCache(filePath, handler, new FakeTimeProvider(Baseline));

            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(result.DegradedWarning).IsNull();
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Default_cache_file_lands_in_the_platform_cache_directory()
    {
        string platformDirectory = CatalogCache.GetPlatformCacheDirectory();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        await Assert.That(Path.GetFileName(platformDirectory)).IsEqualTo("HarnessGateway");
        await Assert.That(CatalogCache.DefaultCacheFilePath)
            .IsEqualTo(Path.Combine(platformDirectory, "models-dev-catalog.json"));

        if (OperatingSystem.IsWindows())
        {
            await Assert.That(
                platformDirectory.StartsWith(localAppData, StringComparison.Ordinal)).IsTrue();
        }

        // The constructor that takes no path writes through to that
        // platform location: one fetch, snapshot present, cleaned up
        // after the test.
        StubHttpMessageHandler handler = new(CatalogJson);
        CatalogCache cache = new(CreateRepository(handler), new FakeTimeProvider(Baseline));
        try
        {
            CatalogCacheResult result = await cache.GetCatalogAsync();

            await Assert.That(result.Catalog).IsNotNull();
            await Assert.That(File.Exists(CatalogCache.DefaultCacheFilePath)).IsTrue();
        }
        finally
        {
            if (File.Exists(CatalogCache.DefaultCacheFilePath))
            {
                File.Delete(CatalogCache.DefaultCacheFilePath);
            }
        }
    }

    [Test]
    public async Task Freshness_window_is_one_hour()
    {
        await Assert.That(CatalogCache.FreshnessWindow).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task Composition_registers_the_models_dev_client_and_the_cache()
    {
        // Mirrors the composition root: the package's client services,
        // then the cache built on the repository they register.
        ServiceCollection services = new();
        services.AddModelsDotDevSharp();
        services.AddSingleton(serviceProvider => new CatalogCache(
            serviceProvider.GetRequiredService<ICatalogRepository>(),
            TimeProvider.System));
        using ServiceProvider provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<ICatalogRepository>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<CatalogCache>()).IsNotNull();
    }
}
