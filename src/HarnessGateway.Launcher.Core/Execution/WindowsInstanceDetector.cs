using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// The Windows running-instance detector: read-only enumeration of the
/// system's processes, matched against the harness's declared identity list.
/// </summary>
/// <remarks>
/// Windows resolves executable names case-insensitively, so the declared
/// names match either way, and a name declared with its <c>.exe</c> suffix
/// matches the extensionless image name the OS reports. Read-only: it starts
/// no process and writes nothing.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstanceDetector : IRunningInstanceDetector
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc />
    public (bool Found, IReadOnlyList<int> ProcessIds) DetectRunningInstances(
        IReadOnlyList<string> processNames)
    {
        HashSet<string> candidates = Normalize(processNames);
        if (candidates.Count == 0)
        {
            return (false, []);
        }

        List<int> matches = [];
        Process[] processes = Process.GetProcesses();
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (candidates.Contains(process.ProcessName))
                    {
                        matches.Add(process.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and the name
                    // read - it cannot be a live instance now.
                }
                catch (Win32Exception)
                {
                    // A process this user cannot inspect: skip it rather
                    // than fail the whole detection pass.
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        return (matches.Count > 0, matches);
    }

    /// <summary>
    /// The declared names reduced to the extensionless image names Windows
    /// reports, so <c>OpenCode.exe</c> and <c>OpenCode</c> are one identity.
    /// </summary>
    private static HashSet<string> Normalize(IReadOnlyList<string> processNames)
    {
        HashSet<string> candidates = new(NameComparer);
        if (processNames is null)
        {
            return candidates;
        }

        foreach (string name in processNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string trimmed = name.Trim();
            candidates.Add(
                trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? trimmed[..^".exe".Length]
                    : trimmed);
        }

        return candidates;
    }
}
