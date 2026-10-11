namespace HarnessGateway.Launcher.Core.Backends.Models;

public sealed record ModelCatalogEntry(string Id);

public sealed record ModelCatalog(IReadOnlyList<ModelCatalogEntry> Models, DateTimeOffset FetchedAt);
