using CliInvoke;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Execution;

public sealed class LaunchRunner : ILaunchRunner
{
    public async Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
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
        ProcessConfiguration config = new(plan.Process.TargetFilePath)
        {
            Arguments = plan.Process.Arguments,
            ArgumentList = plan.Process.ArgumentList,
            WorkingDirectoryPath = plan.Process.WorkingDirectoryPath,
            OutputRedirection = true,
            WindowCreation = plan.Process.WindowCreation,
            UseShellExecution = plan.Process.UseShellExecution,
            EnvironmentVariables = plan.Process.EnvironmentVariables,
            RedirectStandardInput = plan.Process.RedirectStandardInput,
        };

        BufferedProcessResult result = await CliRun.RunBufferedAsync(config, cancellationToken: cancellationToken)
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
}