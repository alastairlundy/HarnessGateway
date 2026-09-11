using CliInvoke;
using CliInvoke.Core;
using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Core.Execution;

public sealed class LaunchRunner
{
    public async Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        var config = new ProcessConfiguration(plan.Process.TargetFilePath)
        {
            Arguments = plan.Process.Arguments,
            ArgumentList = plan.Process.ArgumentList,
            WorkingDirectoryPath = plan.Process.WorkingDirectoryPath,
            OutputRedirection = false,
            WindowCreation = plan.Process.WindowCreation,
            UseShellExecution = plan.Process.UseShellExecution,
            EnvironmentVariables = plan.Process.EnvironmentVariables,
            RedirectStandardInput = plan.Process.RedirectStandardInput
        };

        var result = await CliRun.RunAsync(config, cancellationToken: cancellationToken);
        return result.ExitCode;
    }
}
