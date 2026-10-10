using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class KiloAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "Kilo",
        CommandName: "kilo",
        Aliases: [],
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm i -g @kilocode/cli",
        DetectionCommand: "kilo --version",
        ConfigFileCandidates: ["kilo.json"],
        SupportedProtocols: new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic
        });

    private readonly Func<string, CancellationToken, Task<string?>> _detectCommand;

    public KiloAdapter()
        : this(DetectOnPath)
    {
    }

    internal KiloAdapter(Func<string, CancellationToken, Task<string?>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        string? result = await _detectCommand("kilo", cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> envVars = new(StringComparer.Ordinal);

        string baseUrl = request.Backend.BaseUri.ToString().TrimEnd('/');

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

        List<string> args = [];
        if (request.Model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        ProcessConfiguration config = new("kilo")
        {
            Arguments = args.Count > 0 ? string.Join(" ", args) : "",
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars
        };

        return Task.FromResult(new LaunchPlan(config, []));
    }

    private static async Task<string?> DetectOnPath(string command, CancellationToken cancellationToken)
    {
        try
        {
            string fileName = OperatingSystem.IsWindows() ? "where" : "which";
            ProcessConfiguration config = new(fileName, command)
            {
                OutputRedirection = true
            };

            ProcessResult result = await CliRun.RunAsync(config, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? command : null;
        }
        catch
        {
            return null;
        }
    }
}