namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Maps one resolved protocol and base URL to the env-var set a harness
/// needs, so the mapping exists and is tested exactly once instead of
/// once per adapter. The caller computes the effective protocol before
/// calling; the plain-value inputs keep the mapping pure and fakeable,
/// which lets tests substitute this interface rather than shimming statics.
/// Registration at the composition root lands with the adapter-consumption
/// ticket, not here.
/// </summary>
public interface IProtocolEmissionPlanner
{
    /// <summary>
    /// Plans the env-var emission for <paramref name="protocol"/> as served
    /// from <paramref name="baseUri"/>. Synchronous by design - no async and
    /// no whole-result input.
    /// </summary>
    ProtocolEmission Plan(ProtocolKind protocol, Uri baseUri);
}
