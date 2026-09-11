namespace LmProxy.AgentLauncher.Core.Backends.Models;

public sealed record ModelCatalogEntry(string Id, string? DisplayName);

public sealed record ModelCatalog(IReadOnlyList<ModelCatalogEntry> Models, DateTimeOffset FetchedAt);
