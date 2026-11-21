using CliInvoke;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class ClaudeCodeAdapter : IHarnessAdapter, INonInteractiveLaunchAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "Claude Code",
        CommandName: "claude-code",
        // The binary and the vendor's docs both say "claude", so users reach
        // for that spelling. Accepted as an input token only; help output and
        // the wizard list show "Claude Code".
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

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, printPrompt: null));

    /// <summary>
    /// The one-shot mode: <c>claude -p "&lt;prompt&gt;"</c> - the harness's own
    /// spelling. Everything else (base URL, auth token, model) plans exactly
    /// as the interactive launch does, because -p changes how claude reads
    /// its input, not how it is configured.
    /// </summary>
    public Task<LaunchPlan> PlanNonInteractiveAsync(
        ResolvedLaunch request,
        string prompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, prompt));

    private static LaunchPlan BuildPlan(ResolvedLaunch request, string? printPrompt)
    {
        (Dictionary<string, string> envVars, List<ConfigurationAction> actions) =
            BuildProcessEnvironment(request);

        ProcessConfiguration config = new("claude")
        {
            Arguments = string.Empty,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
            // -p with the prompt on argv is claude's documented one-shot
            // input path. ArgumentList only: when it is non-empty the
            // process library ignores the Arguments string, and a list
            // entry stays one argv element no matter what the prompt
            // contains. An empty list (the interactive launch) changes
            // nothing - it is the property's default.
            ArgumentList = printPrompt is null ? [] : ["-p", printPrompt],
        };

        return new LaunchPlan(config, actions);
    }

    /// <summary>
    /// The emission, the harness-owned key rename and the optional model:
    /// everything that shapes the spawned process's environment rather than
    /// its argv. No config file is ever read or written, so the user's
    /// durable Claude Code settings are untouched and the launcher's own
    /// process environment is never mutated.
    /// </summary>
    private static (Dictionary<string, string> EnvVars, List<ConfigurationAction> Actions)
        BuildProcessEnvironment(ResolvedLaunch request)
    {
        // Direct case branching: each case hands over its own
        // effective protocol, base URL, and model - no stored-looking
        // provider is synthesized in between, and no protocol is computed
        // here.
        (ProtocolEmission emission, string? model) = request.Launch switch
        {
            LaunchResolution.ProviderCase providerCase => (
                request.EmissionPlanner.Plan(providerCase.Protocol, providerCase.BaseUri),
                providerCase.Model),
            LaunchResolution.AdHocCase adHoc => (
                request.EmissionPlanner.Plan(adHoc.Protocol, adHoc.BaseUri),
                adHoc.Model),
            _ => throw new InvalidOperationException(
                $"unexpected launch resolution case '{request.Launch.GetType().Name}'"),
        };

        // The emission's base-url variable merges verbatim -
        // ANTHROPIC_BASE_URL is this harness's spelling too.
        List<ConfigurationAction> actions = [];
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            [emission.BaseUrlVariable] = emission.BaseUrlValue,
        };

        // Token choice (a harness-owned rename, adapter-side):
        // ANTHROPIC_AUTH_TOKEN, never the emission's ANTHROPIC_API_KEY slot -
        // the latter makes Claude Code raise an interactive approval prompt.
        // The key therefore lands under this harness's own name and the
        // planner's slot stays unfilled. Key handling: inject the saved key
        // only when the launch carries one; with no key there is no token
        // variable at all.
        if (request.ApiKey is { Length: > 0 })
        {
            envVars["ANTHROPIC_AUTH_TOKEN"] = request.ApiKey;
        }

        // The model is optional for claude-code. When one is chosen it reaches
        // the harness as ANTHROPIC_MODEL on the spawned process environment,
        // and the plan records the optional --model CLI argument action.
        // No config-file write either way.
        if (model is { Length: > 0 })
        {
            envVars["ANTHROPIC_MODEL"] = model;
            actions.Add(new CliArgumentAction("--model", model));
        }

        return (envVars, actions);
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