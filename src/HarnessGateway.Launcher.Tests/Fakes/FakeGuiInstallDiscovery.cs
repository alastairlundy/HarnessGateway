using HarnessGateway.Launcher.Core.Execution;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted install discovery for flow tests: answers with a fixed
/// found/miss result and remembers the identity list it was asked about, so
/// a test can pin both the outcome and that the harness's declared names
/// were the input.
/// </summary>
public sealed class FakeGuiInstallDiscovery : IGuiInstallDiscovery
{
    private readonly bool _found;
    private readonly string? _executablePath;

    /// <param name="found">What discovery reports: an install exists or not.</param>
    /// <param name="executablePath">The path to report alongside a hit.</param>
    public FakeGuiInstallDiscovery(bool found = false, string? executablePath = null)
    {
        _found = found;
        _executablePath = found ? executablePath : null;
    }

    /// <summary>The identity list of the last discovery request.</summary>
    public IReadOnlyList<string>? LastRequestedNames { get; private set; }

    /// <inheritdoc />
    public (bool Found, string? ExecutablePath) Discover(IReadOnlyList<string> instanceProcessNames)
    {
        LastRequestedNames = instanceProcessNames;
        return (_found, _executablePath);
    }
}
