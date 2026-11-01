using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Execution;

namespace HarnessGateway.Launcher.Tests.Execution;

/// <summary>
/// Pins install discovery against injected fake roots and a fake resolver:
/// neither the resolver-first pass nor the conventional-root pass ever
/// touches a real install location on the runner. The per-OS families differ
/// only in their root facts - <c>ConventionalRoots()</c> supplies those for
/// the running OS - while this one discovery implementation serves every
/// harness.
/// </summary>
public class DiscoveryTests
{
    private static string CreateFakeRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"HarnessGateway-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    // --- Pass 1: the resolver wins before any root is considered ---

    [Test]
    public async Task Resolver_hit_wins_before_any_root_is_considered()
    {
        string root = CreateFakeRoot();
        try
        {
            string resolvedPath = Path.Combine(root, "resolver-preferred");
            FakeFilePathResolver resolver = new(new Dictionary<string, string>
            {
                ["gui-app"] = resolvedPath,
            });

            // The same name also sits under a root, so a pass order slip
            // would report the root's candidate instead of the resolver's.
            File.WriteAllText(Path.Combine(root, "gui-app"), "binary");
            GuiInstallDiscovery discovery = new(resolver, [root]);

            (bool found, string? executablePath) = discovery.Discover(["gui-app"]);

            await Assert.That(found).IsTrue();
            await Assert.That(executablePath).IsEqualTo(resolvedPath);
            await Assert.That(resolver.RequestedNames).IsEquivalentTo(["gui-app"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // --- Pass 2: the conventional-root scan, per-OS-family roots injected ---

    [Test]
    public async Task Root_scan_finds_an_executable_directly_under_a_root()
    {
        string root = CreateFakeRoot();
        try
        {
            string expected = Path.Combine(root, "gui-app");
            File.WriteAllText(expected, "binary");
            GuiInstallDiscovery discovery = new(new FakeFilePathResolver(), [root]);

            (bool found, string? executablePath) = discovery.Discover(["gui-app"]);

            await Assert.That(found).IsTrue();
            await Assert.That(executablePath).IsEqualTo(expected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Root_scan_accepts_an_app_bundle_directory()
    {
        string root = CreateFakeRoot();
        try
        {
            // The macOS shape: the declared name is a bundle directory, not
            // an executable file, and its existence still proves the install.
            string expected = Path.Combine(root, "Gui.app");
            Directory.CreateDirectory(expected);
            GuiInstallDiscovery discovery = new(new FakeFilePathResolver(), [root]);

            (bool found, string? executablePath) = discovery.Discover(["Gui.app"]);

            await Assert.That(found).IsTrue();
            await Assert.That(executablePath).IsEqualTo(expected);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Declared_name_order_governs_the_first_match()
    {
        string root = CreateFakeRoot();
        try
        {
            string second = Path.Combine(root, "second-app");
            string first = Path.Combine(root, "first-app");
            File.WriteAllText(second, "binary");
            File.WriteAllText(first, "binary");
            FakeFilePathResolver resolver = new();
            GuiInstallDiscovery discovery = new(resolver, [root]);

            // Declared order, not alphabetical order, is the first-match
            // rule - in both passes: the resolver is asked in that order
            // too.
            (bool found, string? executablePath) = discovery.Discover(["second-app", "first-app"]);

            await Assert.That(found).IsTrue();
            await Assert.That(executablePath).IsEqualTo(second);
            await Assert.That(resolver.RequestedNames).IsEquivalentTo(["second-app", "first-app"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Exhausted_search_reports_an_honest_miss()
    {
        string root = CreateFakeRoot();
        try
        {
            GuiInstallDiscovery discovery = new(new FakeFilePathResolver(), [root]);

            (bool found, string? executablePath) = discovery.Discover(["gui-app"]);

            await Assert.That(found).IsFalse();
            await Assert.That(executablePath).IsNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Empty_or_blank_declared_names_never_reach_the_resolver_or_the_roots()
    {
        string root = CreateFakeRoot();
        try
        {
            FakeFilePathResolver resolver = new();
            GuiInstallDiscovery discovery = new(resolver, [root]);

            (bool emptyFound, string? emptyPath) = discovery.Discover([]);
            (bool blankFound, string? blankPath) = discovery.Discover(["", "   "]);

            await Assert.That(emptyFound).IsFalse();
            await Assert.That(emptyPath).IsNull();
            await Assert.That(blankFound).IsFalse();
            await Assert.That(blankPath).IsNull();
            await Assert.That(resolver.RequestedNames).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // --- The launcher-owned per-OS root facts ---

    [Test]
    public async Task ConventionalRoots_are_the_running_os_family_conventions()
    {
        if (OperatingSystem.IsWindows())
        {
            IReadOnlyList<string> roots = GuiInstallDiscovery.ConventionalRoots();
            await Assert.That(roots).Contains(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            await Assert.That(roots).Contains(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(GuiInstallDiscovery.ConventionalRoots()).IsEquivalentTo(["/Applications"]);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await Assert.That(GuiInstallDiscovery.ConventionalRoots()).IsEquivalentTo(["/usr/bin"]);
        }
    }

    /// <summary>
    /// Scripted resolver: answers from a fixed name-to-path map through the
    /// no-throw member discovery uses, records every name it was asked
    /// about, and never consults the file system.
    /// </summary>
    private sealed class FakeFilePathResolver : IFilePathResolver
    {
        private readonly IReadOnlyDictionary<string, string> _resolutions;

        public FakeFilePathResolver(IReadOnlyDictionary<string, string>? resolutions = null)
        {
            _resolutions = resolutions ?? new Dictionary<string, string>();
        }

        public List<string> RequestedNames { get; } = [];

        public FileInfo ResolveFilePath(string filePathToResolve)
        {
            return TryResolveFilePath(filePathToResolve, out FileInfo? resolved) && resolved is not null
                ? resolved
                : throw new FileNotFoundException("fake resolver miss", filePathToResolve);
        }

        public bool TryResolveFilePath(string filePathToResolve, out FileInfo? resolvedFilePath)
        {
            RequestedNames.Add(filePathToResolve);
            if (_resolutions.TryGetValue(filePathToResolve, out string? path))
            {
                resolvedFilePath = new FileInfo(path);
                return true;
            }

            resolvedFilePath = null;
            return false;
        }
    }
}
