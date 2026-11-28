using ModelsDotDevSharp;
using ModelsDotDevSharp.Abstractions;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// An in-memory catalog source for the qualification flow tests: it always
/// serves the catalog it was built with and never reaches the network. The
/// cache file path the <see cref="Core.Providers.Models.CatalogCache"/> uses
/// is supplied by the caller, normally under a throwaway test directory.
/// </summary>
public sealed class FakeCatalogRepository : ICatalogRepository
{
    private readonly AICatalog _catalog;

    public FakeCatalogRepository(AICatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>A catalog with no providers, for flows that never qualify.</summary>
    public static AICatalog Empty => new();

    public Task<AICatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog);
}
