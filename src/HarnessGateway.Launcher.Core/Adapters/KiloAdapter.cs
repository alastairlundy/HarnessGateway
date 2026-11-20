using CliInvoke;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class KiloAdapter : IHarnessAdapter, INonInteractiveLaunchAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "Kilo",
        CommandName: "kilo",
        [],
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm i -g @kilocode/cli",
        DetectionCommand: "kilo --version",
        ["kilo.json"],
        new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic,
        },
        new HashSet<ConfigurationSurface>(),
        []);

    private readonly Func<string, CancellationToken, Task<bool>> _detectCommand;

    public KiloAdapter()
        : this(DetectOnPath)
    {
    }

    internal KiloAdapter(Func<string, CancellationToken, Task<bool>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("kilo", cancellationToken);

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, printPrompt: null));

    /// <summary>
    /// The one-shot mode: <c>kilo run --model &lt;m&gt; "&lt;message&gt;"</c> -
    /// the harness's own subcommand for a single message, with the same
    /// -m/--model flag the interactive launch passes.
    /// </summary>
    public Task<LaunchPlan> PlanNonInteractiveAsync(
        ResolvedLaunch request,
        string prompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, prompt));

    private static LaunchPlan BuildPlan(ResolvedLaunch request, string? printPrompt)
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

        // Kilo's openai-responses protocol joins the "/responses" resource
        // path onto the base URL itself, so an endpoint that already ends in
        // the segment would be doubled. Adapter-owned and protocol-gated:
        // the chat and anthropic launch paths keep the endpoint exactly as
        // configured.
        emission = EmissionMerge.DropResponsesSegment(emission);

        // One shared mapping: the emission carries this protocol's
        // env-var names and values, merged by the one consumption rule.
        Dictionary<string, string> envVars = new(StringComparer.Ordinal);
        EmissionMerge.MergeInto(envVars, emission, request.ApiKey);

        ProcessConfiguration config = new("kilo")
        {
            ArgumentList = BuildArguments(printPrompt, model),
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return new LaunchPlan(config, []);
    }

    /// <summary>
    /// The argv for the launch: the model rides -m/--model in the global
    /// slot for the interactive launch, while the one-shot subcommand
    /// <c>kilo run</c> takes the message as its positional and its own
    /// -m/--model flag, so the model goes after <c>run</c> there. The
    /// message is last, as one ArgumentList entry: the process library
    /// keeps it a single argv element whatever it contains.
    /// </summary>
    private static List<string> BuildArguments(string? printPrompt, string? model)
    {
        List<string> args = [];

        if (printPrompt is not null)
        {
            args.Add("run");
            if (model is { Length: > 0 })
            {
                args.Add("--model");
                args.Add(model);
            }

            args.Add(printPrompt);
        }
        else if (model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(model);
        }

        return args;
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