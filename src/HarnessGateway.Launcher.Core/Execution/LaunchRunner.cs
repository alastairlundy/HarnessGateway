using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Execution;

public sealed class LaunchRunner : ILaunchRunner
{
    public async Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        ProcessConfiguration config = new(plan.Process.TargetFilePath)
        {
            Arguments = plan.Process.Arguments,
            ArgumentList = plan.Process.ArgumentList,
            WorkingDirectoryPath = plan.Process.WorkingDirectoryPath,
            OutputRedirection = false,
            WindowCreation = plan.Process.WindowCreation,
            UseShellExecution = plan.Process.UseShellExecution,
            EnvironmentVariables = plan.Process.EnvironmentVariables,
            RedirectStandardInput = plan.Process.RedirectStandardInput,
        };

        ProcessResult result = await CliRun.RunAsync(config, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }
}