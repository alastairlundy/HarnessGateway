using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters.Abstractions;

/// <summary>
/// Optional per-harness capability: the harness has a documented one-shot
/// non-interactive mode the launcher can drive with a prompt, in the
/// harness's own spelling of it (<c>claude -p</c>, <c>opencode run</c>,
/// <c>kilo run</c>, <c>codex exec</c>, <c>copilot -p</c>, <c>pi -p</c>). Adapters opt in
/// by implementing this; the flow refuses <c>-p/--print</c> against anything
/// else instead of guessing at argv.
/// </summary>
public interface INonInteractiveLaunchAdapter
{
    /// <summary>
    /// Plans the one-shot run: the same resolved launch, key, working
    /// directory and emission <see cref="IHarnessAdapter.PlanLaunchAsync"/>
    /// plans with, plus <paramref name="prompt"/> on the argv where the
    /// harness's non-interactive mode expects it. The flow has already
    /// refused a missing or whitespace-only prompt, so the adapter can
    /// treat it as present.
    /// </summary>
    Task<LaunchPlan> PlanNonInteractiveAsync(
        ResolvedLaunch request,
        string prompt,
        CancellationToken cancellationToken = default);
}
