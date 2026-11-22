using HarnessGateway.Launcher.Core.Configuration;

namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// One launch: the harness process to spawn, the configuration actions that
/// must land first, and an optional process the runner runs to completion
/// before that spawn.
/// </summary>
/// <remarks>
/// The pre-launch process is deliberately not a <see cref="ConfigurationAction"/>:
/// configuration actions are writes with a restore - the backup service takes a
/// marker before each one and replays it - while this is a side effect with no
/// undo, and only the runner, which owns process spawning, can execute it. GUI
/// handoff cannot: <see cref="Execution.ILaunchRunner.Handoff"/> returns a pid
/// immediately, so a plan carrying one there fails loudly rather than skipping
/// the step silently.
/// </remarks>
public sealed record LaunchPlan(ProcessConfiguration Process,
    IReadOnlyList<ConfigurationAction> ConfigurationActions,
    ProcessConfiguration? PreLaunchProcess = null);
