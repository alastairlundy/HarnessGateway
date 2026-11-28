using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// One cache read: the catalog when this launch can use one, and the
/// degraded-disambiguation warning to print when it cannot - a fetch
/// failure or a cold cache both leave <see cref="Catalog"/> null, and
/// either way the launch proceeds without qualification.
/// </summary>
public sealed record CatalogCacheResult(AICatalog? Catalog, string? DegradedWarning);
