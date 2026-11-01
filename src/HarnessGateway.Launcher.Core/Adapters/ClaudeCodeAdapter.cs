using CliInvoke;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class ClaudeCodeAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "claude-code",
        CommandName: "claude-code",
        // The binary and the vendor's docs both say "claude", so users reach
        // for that spelling. Accepted as an input token only; help output and
        // the wizard list show "claude-code".
        ["claude"],
        Kind: HarnessKind.Cli,
        RequiresModel: false,
        "Install via: npm i -g @anthropic-ai/claude-code. Note: pointing Claude Code at a "
        + "non-first-party base URL disables MCP (Model Context Protocol) tool search and Remote "
        + "Control - that is a Claude Code consequence, not an action by the launcher.",
        DetectionCommand: "claude --version",
        [],
        new HashSet<ProtocolKind>
        {
            ProtocolKind.Anthropic,
        },
        new HashSet<ConfigurationSurface>(),
        []);

    private readonly Func<string, CancellationToken, Task<bool>> _detectCommand;

    public ClaudeCodeAdapter()
        : this(DetectOnPath)
    {
    }

    internal ClaudeCodeAdapter(Func<string, CancellationToken, Task<bool>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("claude", cancellationToken);

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
    {
        // claude-code is configured exclusively through environment variables
        // scoped to the spawned process. No config file is ever read or written,
        // so the user's durable Claude Code settings are untouched and the
        // launcher's own process environment is never mutated.
        List<ConfigurationAction> actions = [];
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            ["ANTHROPIC_BASE_URL"] = request.Provider.BaseUri.ToString().TrimEnd('/'),
        };

        // Token choice: ANTHROPIC_AUTH_TOKEN, never ANTHROPIC_API_KEY - the
        // latter makes Claude Code raise an interactive approval prompt. Key
        // handling: inject the saved key only when the resolved backend entry
        // has one; with no key saved there is no token variable at all.
        if (request.Provider.ApiKey is { Length: > 0 })
        {
            envVars["ANTHROPIC_AUTH_TOKEN"] = request.Provider.ApiKey;
        }

        // The model is optional for claude-code. When one is chosen it reaches
        // the harness as ANTHROPIC_MODEL on the spawned process environment,
        // and the plan records the optional --model CLI argument action.
        // No config-file write either way.
        if (request.Model is { Length: > 0 })
        {
            envVars["ANTHROPIC_MODEL"] = request.Model;
            actions.Add(new CliArgumentAction("--model", request.Model));
        }

        ProcessConfiguration config = new("claude")
        {
            Arguments = string.Empty,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return Task.FromResult(new LaunchPlan(config, actions));
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

            ProcessResult result = await CliRun.RunAsync(config, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}