using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class CopilotCliAdapter : IHarnessAdapter, INonInteractiveLaunchAdapter
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
        },
        new HashSet<ConfigurationSurface>(),
        []);

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

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, printPrompt: null));

    /// <summary>
    /// The one-shot mode: <c>copilot -p "&lt;text&gt;"</c> - the harness's
    /// documented non-interactive flag (its help names -p/--prompt for
    /// non-interactive scripting). Configuration plans exactly as the
    /// interactive launch does.
    /// </summary>
    public Task<LaunchPlan> PlanNonInteractiveAsync(
        ResolvedLaunch request,
        string prompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, prompt));

    private static LaunchPlan BuildPlan(ResolvedLaunch request, string? printPrompt)
    {
        (ProtocolEmission emission, string? model) = ResolveEmission(request);

        // One shared mapping: the emission carries this protocol's
        // base URL and protocol. This harness reads its own provider-block
        // names (harness-owned, adapter-side), so the block below
        // supplies the variable names and the emission supplies the values -
        // the plan therefore carries exactly this harness's three variables
        // and never the canonical OPENAI_*/ANTHROPIC_* spellings.
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            ["COPILOT_PROVIDER_BASE_URL"] = emission.BaseUrlValue,
            ["COPILOT_PROVIDER_TYPE"] = emission.Protocol switch
            {
                ProtocolKind.Anthropic => "anthropic",
                _ => "openai",
            },
        };

        if (request.ApiKey is { Length: > 0 })
        {
            envVars["COPILOT_PROVIDER_API_KEY"] = request.ApiKey;
        }

        List<string> args = [];
        if (model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(model);
        }

        if (printPrompt is not null)
        {
            // -p/--prompt is this harness's non-interactive flag; the text
            // goes into ArgumentList so it stays one argv element.
            args.Add("-p");
            args.Add(printPrompt);
        }

        ProcessConfiguration config = new("copilot")
        {
            ArgumentList = args,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return new LaunchPlan(config, []);
    }

    /// <summary>
    /// Direct case branching: each case hands over its own effective
    /// protocol, base URL, and model - no stored-looking provider is
    /// synthesized in between, and no protocol is computed here.
    /// </summary>
    private static (ProtocolEmission Emission, string? Model) ResolveEmission(ResolvedLaunch request) =>
        request.Launch switch
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
