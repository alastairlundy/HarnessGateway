namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

/// <summary>
/// A mechanism a harness can be handed launcher configuration through.
/// Closed set: configuration planning only ever picks from these three.
/// </summary>
public enum ConfigurationSurface
{
    /// <summary>Environment variables carried on the launched process.</summary>
    Environment,

    /// <summary>Command-line arguments the harness accepts.</summary>
    Flags,

    /// <summary>Configuration files the launcher writes.</summary>
    Files,
}
