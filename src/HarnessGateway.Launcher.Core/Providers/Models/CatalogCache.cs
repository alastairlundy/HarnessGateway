using System.Text.Json;
using System.Text.Json.Serialization;
using ModelsDotDevSharp;
using ModelsDotDevSharp.Abstractions;

namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// The launcher's own view of the models.dev catalog - the harness-side
/// catalog bare model names get qualified against at the launch handoff.
/// One instance owns the whole cache lifecycle: load, staleness
/// evaluation against the freshness window, the fetch through
/// ModelsDotDevSharp, and save.
///
/// Fetching is best-effort and a launch never fails on it: on a fetch
/// failure or a cold cache the caller gets a null catalog plus
/// <see cref="DegradedWarning"/> to print, so the launch proceeds
/// without qualification instead of dying on the network. A failed
/// refresh never deletes the snapshot already on disk, and the
/// per-instance memo means at most one fetch happens per launch - the
/// composition registers one instance per process.
/// </summary>
public sealed partial class CatalogCache
{
    /// <summary>
    /// The freshness window: a snapshot older than one hour is stale and
    /// triggers a refetch. Deliberately a compile-time constant with no
    /// CLI or config surface - the one-hour promise is documented, not
    /// tunable.
    /// </summary>
    private const int FreshnessWindowHours = 1;

    /// <summary>
    /// The warning the caller prints when this launch runs without a
    /// catalog: it names the degraded disambiguation so a bare model
    /// name passing through unqualified never reads as a silent success.
    /// </summary>
    public const string DegradedWarning =
        "Model/provider disambiguation degraded: the models.dev catalog is unavailable, so bare model names pass through unqualified.";

    /// <summary>How long a saved snapshot stays usable before a refetch.</summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(FreshnessWindowHours);

    private const string CacheFileName = "models-dev-catalog.json";

    private readonly ICatalogRepository _repository;
    private readonly TimeProvider _clock;
    private readonly string _cacheFilePath;

    // The one-fetch-per-launch rule: the first call that needs the
    // network spends it, and every later call in this process lives
    // with the outcome.
    private bool _fetchAttempted;
    private AICatalog? _fetchedThisLaunch;

    public CatalogCache(
        ICatalogRepository repository,
        TimeProvider clock,
        string? cacheFilePath = null)
    {
        _repository = repository;
        _clock = clock;
        _cacheFilePath = cacheFilePath ?? DefaultCacheFilePath;
    }

    /// <summary>
    /// The platform cache directory the snapshot lives under, in a
    /// HarnessGateway-specific subfolder: the LocalAppData cache location
    /// on Windows, the XDG cache home on Linux (falling back to
    /// <c>~/.cache</c>), and the Caches directory on macOS - the OS's own
    /// location for reproducible, non-essential data rather than the
    /// settings directory.
    /// </summary>
    public static string GetPlatformCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HarnessGateway");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Caches",
                "HarnessGateway");
        }

        // Linux and others, mirroring how the settings store resolves its
        // XDG base: honour the environment override when it is set.
        string? xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdgCacheHome))
        {
            return Path.Combine(xdgCacheHome, "HarnessGateway");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "HarnessGateway");
    }

    /// <summary>The snapshot's home unless a caller overrides it (tests do).</summary>
    public static string DefaultCacheFilePath => Path.Combine(GetPlatformCacheDirectory(), CacheFileName);

    /// <summary>
    /// Returns the catalog for this launch: a fresh snapshot is served as
    /// it stands, a stale one triggers at most one fetch, and a fetch
    /// that fails - like a cache that has never been written - comes back
    /// as a null catalog with <see cref="DegradedWarning"/> for the
    /// caller to print. Only a caller-initiated cancellation escapes as
    /// an exception; every network and parsing fault degrades.
    /// </summary>
    public async Task<CatalogCacheResult> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_fetchedThisLaunch is { } fetched)
        {
            return new CatalogCacheResult(fetched, DegradedWarning: null);
        }

        CatalogCacheEntry? cached = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null && _clock.GetUtcNow() - cached.FetchedAt <= FreshnessWindow)
        {
            return new CatalogCacheResult(cached.Catalog, DegradedWarning: null);
        }

        if (_fetchAttempted)
        {
            // This launch already spent its fetch and the snapshot on
            // disk is stale (or absent): proceed without qualification.
            // The stale file stays exactly as it is - a network fault
            // never deletes cached data.
            return new CatalogCacheResult(null, DegradedWarning);
        }

        _fetchAttempted = true;

        try
        {
            AICatalog catalog = await _repository.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
            _fetchedThisLaunch = catalog;
            return new CatalogCacheResult(catalog, DegradedWarning: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled; cancellation is not a degraded catalog
            // and must never be mapped onto the launch outcome.
            throw;
        }
        catch (Exception)
        {
            // Unreachable host, HTTP error, or a payload the reader
            // rejects: degrade, keep any stale snapshot, warn. The file
            // was never touched, so the cache is exactly as it was.
            return new CatalogCacheResult(null, DegradedWarning);
        }
    }

    /// <summary>
    /// Reads the snapshot, or returns null when there is none: missing
    /// file, unreadable file, or content the reader rejects all count as
    /// a cold cache and cost at most one refetch.
    /// </summary>
    private async Task<CatalogCacheEntry?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CatalogCacheJsonContext.Default.CatalogCacheEntry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the snapshot with the fetch stamp staleness is measured
    /// against. A write that fails only costs the next launch a refetch:
    /// the catalog fetched above still serves this one, so an I/O fault
    /// here degrades nothing.
    /// </summary>
    private async Task SaveAsync(AICatalog catalog, CancellationToken cancellationToken)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            CatalogCacheEntry entry = new(catalog, _clock.GetUtcNow());
            string json = JsonSerializer.Serialize(entry, CatalogCacheJsonContext.Default.CatalogCacheEntry);
            await File.WriteAllTextAsync(_cacheFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The persisted snapshot: the catalog plus the fetch stamp staleness
    /// is evaluated against, so the stamp comes from the injected clock
    /// rather than the file system's.
    /// </summary>
    private sealed record CatalogCacheEntry(AICatalog Catalog, DateTimeOffset FetchedAt);

    /// <summary>
    /// Source-generated serialization for the cache file. The catalog
    /// model itself comes from ModelsDotDevSharp; this only persists the
    /// envelope the launcher owns, and reading the file back through the
    /// same context keeps the round trip symmetric and AOT-safe.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(CatalogCacheEntry))]
    private sealed partial class CatalogCacheJsonContext : JsonSerializerContext;
}
