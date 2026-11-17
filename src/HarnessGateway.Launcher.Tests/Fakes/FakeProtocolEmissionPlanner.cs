using HarnessGateway.Launcher.Core.Providers;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// <see cref="IProtocolEmissionPlanner"/> test double: records every
/// <c>Plan</c> call so a test can assert which case values an adapter
/// handed over, and returns <see cref="CannedEmission"/> when one is set -
/// a sentinel emission proves the adapter consumes whatever the planner
/// returns instead of computing its own variables. With no canned
/// emission set it delegates to the real mapping, so tests that pin the
/// real variable names still get them through the interface.
/// </summary>
public sealed class FakeProtocolEmissionPlanner : IProtocolEmissionPlanner
{
    private readonly ProtocolEmissionPlanner _real = new();

    /// <summary>The emission to return verbatim; <c>null</c> runs the real mapping.</summary>
    public ProtocolEmission? CannedEmission { get; set; }

    /// <summary>Every <c>(protocol, baseUri)</c> pair an adapter planned with.</summary>
    public List<(ProtocolKind Protocol, Uri BaseUri)> PlannedCalls { get; } = [];

    public ProtocolEmission Plan(ProtocolKind protocol, Uri baseUri)
    {
        PlannedCalls.Add((protocol, baseUri));
        return CannedEmission ?? _real.Plan(protocol, baseUri);
    }
}
