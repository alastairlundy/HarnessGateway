using CliInvoke;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Execution;

public sealed class LaunchRunner : ILaunchRunner
{
    public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        // No exit configuration of our own: the process library's graceful
        // default applies, timeout policy included.
        return RunBufferedAndEchoAsync(
            BuildProcessConfiguration(plan, outputRedirection: true),
            exitConfiguration: null,
            cancellationToken);
    }

    public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        // The graceful default in every respect but the timeout: its policy is
        // replaced with ProcessTimeoutPolicy.None, so the child is never
        // interrupted or killed on a clock. Cancellation and every other exit
        // behaviour stay exactly as the default carries them.
        return RunBufferedAndEchoAsync(
            BuildProcessConfiguration(plan, outputRedirection: true),
            ProcessExitConfiguration.CreateGraceful(ProcessTimeoutPolicy.None),
            cancellationToken);
    }

    public int Handoff(LaunchPlan plan)
    {
        // Detached start, so output redirection is off: there is no reader
        // here to echo buffers, and the caller does not wait for an exit
        // code - it wants the process id and nothing else. Sync-honest on
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
        // unchanged.
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

    private static ProcessConfiguration BuildProcessConfiguration(LaunchPlan plan, bool outputRedirection)
    {
        return new(plan.Process.TargetFilePath)
        {
            Arguments = plan.Process.Arguments,
            ArgumentList = plan.Process.ArgumentList,
            WorkingDirectoryPath = plan.Process.WorkingDirectoryPath,
            OutputRedirection = outputRedirection,
            WindowCreation = plan.Process.WindowCreation,
            UseShellExecution = plan.Process.UseShellExecution,
            EnvironmentVariables = plan.Process.EnvironmentVariables,
            RedirectStandardInput = plan.Process.RedirectStandardInput,
        };
    }
}