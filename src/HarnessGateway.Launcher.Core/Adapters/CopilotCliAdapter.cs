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
        ["github-copilot-cli", "copilot-cli"],
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm i -g @github/copilot",
        DetectionCommand: "copilot --version",
        [],
        new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic,
        });

    private readonly Func<string, CancellationToken, Task<bool>> _detectCommand;

    public CopilotCliAdapter()
        : this(DetectOnPath)
    {
    }

    internal CopilotCliAdapter(Func<string, CancellationToken, Task<bool>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("copilot", cancellationToken);

    public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            ["COPILOT_PROVIDER_BASE_URL"] = request.Backend.BaseUri.ToString().TrimEnd('/'),
        };

        if (request.Backend.ApiKey is { Length: > 0 })
            envVars["COPILOT_PROVIDER_API_KEY"] = request.Backend.ApiKey;

        envVars["COPILOT_PROVIDER_TYPE"] = request.Backend.Protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => "openai",
        };

        List<string> args = [];
        if (request.Model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        ProcessConfiguration config = new("copilot")
        {
            ArgumentList = args,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return Task.FromResult(new LaunchPlan(config, []));
    }

    private static async Task<bool> DetectOnPath(string command, CancellationToken cancellationToken)
    {
        try
        {
            string fileName = OperatingSystem.IsWindows() ? "where" : "which";
            ProcessConfiguration config = new(fileName, command)
            {
                OutputRedirection = true,
            };

            ProcessResult result = await CliRun.RunAsync(config, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
