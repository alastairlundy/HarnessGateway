using CliInvoke.Core;

namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// The shipped install discovery: resolver-first, then the current OS's
/// conventional install roots, then an honest miss. Neither pass writes to
/// disk, starts a process, or depends on adapter-declared paths - the declared
/// identity strings are the only per-harness input, and the roots are
/// launcher-owned operating-system facts, so one implementation serves every
/// harness.
/// </summary>
/// <remarks>
/// Deliberately shallow: an executable must sit where a conventional install
/// puts it (directly under a root), and a miss never throws - it is the input
/// the caller's refusal is built from.
/// </remarks>
public sealed class GuiInstallDiscovery : IGuiInstallDiscovery
{
    private readonly IFilePathResolver _resolver;
    private readonly IReadOnlyList<string> _conventionalRoots;

    /// <summary>
    /// Production wiring: the default CliInvoke resolver and this OS's real
    /// conventional roots.
    /// </summary>
    public GuiInstallDiscovery(IFilePathResolver resolver)
        : this(resolver, ConventionalRoots())
    {
    }

    /// <summary>
    /// Test seam: substitute fake roots to pin per-OS resolution without
    /// touching the machine's real install locations. The roots are still
    /// launcher-owned facts - never harness data.
    /// </summary>
    public GuiInstallDiscovery(IFilePathResolver resolver, IReadOnlyList<string> conventionalRoots)
    {
        _resolver = resolver;
        _conventionalRoots = conventionalRoots;
    }

    /// <inheritdoc />
    public (bool Found, string? ExecutablePath) Discover(IReadOnlyList<string> instanceProcessNames)
    {
        if (instanceProcessNames is null || instanceProcessNames.Count == 0)
        {
            return (false, null);
        }

        // Pass 1: PATH (and the resolver's own directory fallback) via the
        // no-throw variant, in declared-name order.
        foreach (string name in instanceProcessNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (_resolver.TryResolveFilePath(name, out FileInfo? resolved) && resolved is not null)
            {
                return (true, resolved.FullName);
            }
        }

        // Pass 2: conventional roots, still honouring declared-name order so
        // the same first-match rule governs both passes.
        foreach (string name in instanceProcessNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach (string root in _conventionalRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, name);

                // A declared name may be an executable file or - on macOS - an
                // app bundle directory; either proves the app is installed.
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return (true, candidate);
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// The conventional install roots of the OS this process runs on:
    /// <c>Program Files</c> and <c>Program Files (x86)</c> on Windows,
    /// <c>/Applications</c> on macOS, and <c>/usr/bin</c> on Linux. These are
    /// operating-system conventions, not harness facts - there is exactly one
    /// list, shared by every harness discovery runs for.
    /// </summary>
    public static IReadOnlyList<string> ConventionalRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ];
        }

        return OperatingSystem.IsMacOS()
            ? ["/Applications"]
            : ["/usr/bin"];
    }
}
