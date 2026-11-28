using HarnessGateway.Launcher.Core.Providers.Models;
using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// The three qualification collaborators every <see cref="Core.Launch.LaunchFlow"/>
/// construction needs. Flows whose adapters do not declare the own-catalog
/// capability never consult the cache, but the dependencies are still supplied
/// explicitly so a missing composition cannot be hidden by a default value.
/// The cache file lives under the caller's throwaway directory so a test run
/// never touches the real catalog cache on disk.
/// </summary>
public static class QualificationDeps
{
    public static CatalogCache Cache(string tempDir, AICatalog? catalog = null) =>
        new(
            new FakeCatalogRepository(catalog ?? FakeCatalogRepository.Empty),
            TimeProvider.System,
            Path.Combine(tempDir, "models-dev-catalog.json"));

    public static ModelQualifier Qualifier { get; } = new();
}
