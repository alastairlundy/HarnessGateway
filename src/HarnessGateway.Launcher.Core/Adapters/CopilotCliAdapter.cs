using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class CopilotCliAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        // Display name is the vendor's full product name; the token is the
        // binary's own name. Users type `copilot`, the wizard and the refusal
        // prose say `GitHub Copilot CLI`, and neither has to compromise.
        Name: "GitHub Copilot CLI",
        CommandName: "copilot",
        Aliases: ["github-copilot-cli", "copilot-cli"],
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm i -g @github/copilot",
        DetectionCommand: "copilot --version",
        ConfigFileCandidates: [],
        SupportedProtocols: new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic
        });

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
        string? result = await _detectCommand("copilot", cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            ["COPILOT_PROVIDER_BASE_URL"] = request.Backend.BaseUri.ToString().TrimEnd('/')
        };

        if (request.Backend.ApiKey is { Length: > 0 })
            envVars["COPILOT_PROVIDER_API_KEY"] = request.Backend.ApiKey;

        envVars["COPILOT_PROVIDER_TYPE"] = request.Backend.Protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => "openai"
        };

        List<string> args = new();
        if (request.Model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        ProcessConfiguration config = new("copilot")
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
