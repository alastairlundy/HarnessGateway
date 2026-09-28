namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Thrown when an adapter refuses to plan a launch because existing user-owned
/// configuration conflicts with what the launcher would write. The user can resolve
/// the conflict themselves, so the launch path surfaces the message as its own exit
/// code (ConfigConflict) instead of the generic failure path.
/// </summary>
public sealed class LaunchRefusalException(string message) : Exception(message);
