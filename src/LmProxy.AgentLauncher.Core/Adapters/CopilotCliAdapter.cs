using CliInvoke.Core;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Core.Adapters;

public sealed class CopilotCliAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "GitHub Copilot CLI",
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm i -g @github/copilot",
        DetectionCommand: "copilot --version",
        ConfigFileCandidates: []);

    private readonly Func<string, CancellationToken, Task<string?>> _detectCommand;

    public CopilotCliAdapter()
        : this(DetectOnPath)
    {
    }

    internal CopilotCliAdapter(Func<string, CancellationToken, Task<string?>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _detectCommand("copilot", cancellationToken);
        return result is not null;
    }

    public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var actions = new List<ConfigurationAction>();
        var envVars = new Dictionary<string, string>();

        envVars["COPILOT_PROVIDER_BASE_URL"] = request.Backend.BaseUri.ToString().TrimEnd('/');

        if (request.Backend.ApiKey is { Length: > 0 })
            envVars["COPILOT_PROVIDER_API_KEY"] = request.Backend.ApiKey;

        envVars["COPILOT_PROVIDER_TYPE"] = request.Backend.Protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => "openai"
        };

        var args = new List<string>();
        if (request.Model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        var config = new ProcessConfiguration("copilot")
        {
            Arguments = args.Count > 0 ? string.Join(" ", args) : "",
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars
        };

        return Task.FromResult(new LaunchPlan(config, actions));
    }

    private static async Task<string?> DetectOnPath(string command, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? command : null;
        }
        catch
        {
            return null;
        }
    }
}
