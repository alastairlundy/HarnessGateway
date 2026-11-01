using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace HarnessGateway.Launcher.Core.Execution;

/// <summary>
/// The Linux running-instance detector: read-only enumeration of the
/// system's processes, matched against the harness's declared identity list.
/// </summary>
/// <remarks>
/// Linux process names are case-sensitive, so the declared names match
/// exactly here - the per-OS case rule this implementation owns. Read-only:
/// it starts no process and writes nothing, consulting the kernel's process
/// table through the platform APIs only.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxInstanceDetector : IRunningInstanceDetector
{
    private static readonly StringComparer NameComparer = StringComparer.Ordinal;

    /// <inheritdoc />
    public (bool Found, IReadOnlyList<int> ProcessIds) DetectRunningInstances(
        IReadOnlyList<string> processNames)
    {
        HashSet<string> candidates = new(NameComparer);
        if (processNames is not null)
        {
            foreach (string name in processNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    candidates.Add(name.Trim());
                }
            }
        }

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
}
