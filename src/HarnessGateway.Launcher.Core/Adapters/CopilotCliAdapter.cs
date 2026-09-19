using CliInvoke;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

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
            var fileName = OperatingSystem.IsWindows() ? "where" : "which";
            var config = new ProcessConfiguration(fileName, command)
            {
                OutputRedirection = true
            };

            var result = await CliRun.RunAsync(config, cancellationToken: cancellationToken);
            return result.ExitCode == 0 ? command : null;
        }
        catch
        {
            return null;
        }
    }
}
