using HarnessGateway.Launcher.Core.Execution;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted running-instance detector for flow tests: reports a fixed
/// live-instance answer with chosen process ids, and remembers the identity
/// list it was asked about.
/// </summary>
public sealed class FakeRunningInstanceDetector : IRunningInstanceDetector
{
    private readonly bool _found;
    private readonly IReadOnlyList<int> _processIds;

    /// <param name="found">Whether a live instance is reported.</param>
    /// <param name="processIds">
    /// The ids to report for a hit; defaults to a stand-in id when one is
    /// found and to empty when not.
    /// </param>
    public FakeRunningInstanceDetector(bool found = false, IReadOnlyList<int>? processIds = null)
    {
        _found = found;
        _processIds = processIds ?? (found ? [4242] : []);
    }

    /// <summary>The identity list of the last detection request.</summary>
    public IReadOnlyList<string>? LastRequestedNames { get; private set; }

    /// <inheritdoc />
    public (bool Found, IReadOnlyList<int> ProcessIds) DetectRunningInstances(
        IReadOnlyList<string> processNames)
    {
        LastRequestedNames = processNames;
        return (_found, _processIds);
    }
}
