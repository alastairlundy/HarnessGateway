using CliInvoke;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class PiAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "Pi",
        Kind: HarnessKind.Cli,
        RequiresModel: false,
        InstallHint: "Install via: npm i -g @earendil-works/pi-coding-agent",
        DetectionCommand: "pi --version",
        ConfigFileCandidates: [],
        SupportedProtocols: new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic
        });

    private readonly Func<string, CancellationToken, Task<string?>> _detectCommand;

    public PiAdapter()
        : this(DetectOnPath)
    {
    }

    internal PiAdapter(Func<string, CancellationToken, Task<string?>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _detectCommand("pi", cancellationToken);
        return result is not null;
    }

    public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var actions = new List<ConfigurationAction>();
        var envVars = new Dictionary<string, string>();

        var baseUrl = request.Backend.BaseUri.ToString().TrimEnd('/');

        switch (request.Backend.Protocol)
        {
            case ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses:
                envVars["OPENAI_BASE_URL"] = baseUrl;
                if (request.Backend.ApiKey is { Length: > 0 })
                    envVars["OPENAI_API_KEY"] = request.Backend.ApiKey;
                break;
            case ProtocolKind.Anthropic:
                envVars["ANTHROPIC_BASE_URL"] = baseUrl;
                if (request.Backend.ApiKey is { Length: > 0 })
                    envVars["ANTHROPIC_API_KEY"] = request.Backend.ApiKey;
                break;
        }

        var args = new List<string>();
        if (request.Model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        var config = new ProcessConfiguration("pi")
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
