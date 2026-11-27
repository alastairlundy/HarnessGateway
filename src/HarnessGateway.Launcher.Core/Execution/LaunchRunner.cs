using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Execution;

public sealed class LaunchRunner : ILaunchRunner
{
    // The pre-launch step exists to clear the way for the harness, not to
    // gate it, so it gets a tight time bound: a wedged daemon stop must not hold
    // the launch open. Ten seconds is far beyond the ~0.4s a real
    // `app-server daemon stop` takes, including the not-running case.
    private const int PreLaunchTimeoutSeconds = 10;

    public async Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        await RunPreLaunchProcessAsync(plan, cancellationToken).ConfigureAwait(false);

        // No exit configuration of our own: the process library's default
        // applies - a graceful cancellation exit under its standard timeout
        // policy, with unexpected-exit exceptions left to propagate.
        return await RunBufferedAndEchoAsync(
            BuildProcessConfiguration(plan, outputRedirection: true),
            exitConfiguration: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        await RunPreLaunchProcessAsync(plan, cancellationToken).ConfigureAwait(false);

        // The graceful default in every respect but the timeout: its policy is
        // replaced with ProcessTimeoutPolicy.None, so the child is never
        // interrupted or killed on a clock. Cancellation and every other exit
        // behaviour stay exactly as the default carries them.
        return await RunBufferedAndEchoAsync(
            BuildProcessConfiguration(plan, outputRedirection: true),
            ProcessExitConfiguration.CreateGraceful(ProcessTimeoutPolicy.None),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunInteractiveAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        await RunPreLaunchProcessAsync(plan, cancellationToken).ConfigureAwait(false);

        // Console-shared, not buffered: the child inherits this process's
        // standard handles, so a TUI draws on the user's terminal and reads
        // their keystrokes, and nothing is captured to echo later. No
        // timeout either - the session lasts as long as the user does. The
        // flow only dispatches here after its terminal gate, because in a
        // piped context these handles are pipes: the child's output would go
        // nowhere readable (the silent failure the buffered path exists to
        // prevent). The configuration itself is InteractiveConfiguration -
        // including window creation, without which the child paints into a
        // hidden console nobody can see.
        ProcessResult result = await CliRun.RunAsync(
            InteractiveConfiguration(plan),
            ProcessExitConfiguration.CreateGraceful(ProcessTimeoutPolicy.None),
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    /// <summary>
    /// Runs the plan's optional pre-launch process to completion before the
    /// harness spawns. Best effort by contract: a non-zero exit, a spawn
    /// failure, or a hang bounded below never fails the launch, because the
    /// step exists to improve the odds for the harness rather than to gate
    /// it. Cancellation is the one fault that propagates - it is the caller
    /// saying this launch is over.
    /// </summary>
    private static async Task RunPreLaunchProcessAsync(
        LaunchPlan plan,
        CancellationToken cancellationToken)
    {
        ProcessConfiguration? preLaunch = plan.PreLaunchProcess;
        if (preLaunch is null)
        {
            return;
        }

        try
        {
            // Buffered rather than console-shared: the step's output is
            // diagnostics, not the harness session, and the interactive
            // path must not write into the terminal the TUI is about to
            // draw on.
            await CliRun.RunBufferedAsync(
                preLaunch,
                ProcessExitConfiguration.CreateGraceful(
                    ProcessTimeoutPolicy.FromTimeSpan(
                        TimeSpan.FromSeconds(PreLaunchTimeoutSeconds),
                        ProcessExitBehaviour.GracefulExit)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Nothing to stop, nothing on PATH, or the bound was reached:
            // carry on and let the harness report what it sees.
        }
    }

    /// <summary>
    /// The interactive path's exact child configuration: no output
    /// redirection (the child inherits this process's handles) and window
    /// creation ON. Window creation matters on Windows and only here:
    /// with it off - the property default - CliInvoke spawns the child
    /// with no console <em>window</em>, so the handles are still console
    /// handles (GetConsoleMode succeeds) while <c>GetConsoleWindow()</c>
    /// is 0: a TUI believes it has a terminal and paints into a console
    /// nobody can see, leaving the launcher's own window at a bare caret.
    /// Verified child window handles: 0 with the default, a real window
    /// with <c>true</c>; a directly-spawned child shares the parent's
    /// window. On Unix the flag is inert - the child's std handles are
    /// inherited either way.
    /// </summary>
    internal static ProcessConfiguration InteractiveConfiguration(LaunchPlan plan) =>
        BuildProcessConfiguration(plan, outputRedirection: false, windowCreation: true);

    public int Handoff(LaunchPlan plan)
    {
        // The detached path returns a pid immediately, so there is no point
        // at which a pre-launch process could run - and silently skipping
        // it would recreate the exact silent gap the step exists to close.
        // GUI adapters never plan one; a future one that does must fail
        // loudly here instead of launching unprepared.
        if (plan.PreLaunchProcess is not null)
        {
            throw new InvalidOperationException(
                "a detached handoff cannot run the plan's pre-launch process; "
                + "a GUI launch must carry what it needs as configuration "
                + "actions or environment variables instead.");
        }

        // Detached start, so output redirection is off: there is no reader
        // here to echo buffers, and the caller does not wait for an exit
        // code - it wants the process id and nothing else. Synchronous on
        // purpose: the library call is synchronous, and a detached start
        // takes no cancellation token because it is not cancelable. A start
        // failure throws here; folding it into a launch outcome belongs to
        // the flow.
        return CliRun.FireAndForget(BuildProcessConfiguration(plan, outputRedirection: false));
    }

    private static async Task<int> RunBufferedAndEchoAsync(
        ProcessConfiguration config,
        ProcessExitConfiguration? exitConfiguration,
        CancellationToken cancellationToken)
    {
        // Spawn buffered, not console-shared: on .NET an unbuffered child
        // (OutputRedirection=false) inherits the parent console's screen
        // buffer instead of the parent's own standard handles, so its output
        // never reaches this process's stdout/stderr - in piped contexts
        // (scripts, CI, tool embedding) the harness then appears to fail or
        // hang silently with no trace of what the child printed. Capturing
        // and echoing guarantees the child's terminal output surfaces in
        // every context while exit codes and the environment carry over
        // unchanged. (The other half of the unbuffered story - a child
        // spawned without a console window on Windows - is why the
        // interactive path sets WindowCreation; see
        // InteractiveConfiguration.)
        BufferedProcessResult result = await CliRun.RunBufferedAsync(config, exitConfiguration, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            await Console.Out.WriteAsync(result.StandardOutput).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            await Console.Error.WriteAsync(result.StandardError).ConfigureAwait(false);
        }

        return result.ExitCode;
    }

    private static ProcessConfiguration BuildProcessConfiguration(
        LaunchPlan plan,
        bool outputRedirection,
        bool windowCreation = false)
    {
        return new(plan.Process.TargetFilePath)
        {
            Arguments = plan.Process.Arguments,
            ArgumentList = plan.Process.ArgumentList,
            WorkingDirectoryPath = plan.Process.WorkingDirectoryPath,
            OutputRedirection = outputRedirection,
            WindowCreation = windowCreation || plan.Process.WindowCreation,
            UseShellExecution = plan.Process.UseShellExecution,
            EnvironmentVariables = plan.Process.EnvironmentVariables,
            RedirectStandardInput = plan.Process.RedirectStandardInput,
        };
    }
}