using CliInvoke;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class PiAdapter : IHarnessAdapter, INonInteractiveLaunchAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "Pi",
        CommandName: "pi",
        [],
        Kind: HarnessKind.Cli,
        RequiresModel: false,
        InstallHint: "Install via: npm i -g @earendil-works/pi-coding-agent",
        DetectionCommand: "pi --version",
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

    public PiAdapter()
        : this(DetectOnPath)
    {
    }

    internal PiAdapter(Func<string, CancellationToken, Task<bool>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("pi", cancellationToken);

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildPlan(request, printPrompt: null));

    /// <summary>
    /// The one-shot mode: <c>pi -p "&lt;prompt&gt;"</c> - pi's own spelling.
    /// <c>-p</c>/<c>--print</c> is a boolean mode flag ("print response and
    /// exit"), not a prompt-taking one: the prompt rides as the message
    /// positional after it, exactly as <c>pi -p "Summarize this
    /// codebase"</c> is written in pi's CLI reference. Everything else
    /// (base URL, key, model) plans exactly as the interactive launch does,
    /// because -p changes how pi reads its input, not how it is configured.
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

        // Pi's openai-responses API family posts to {baseURL}/responses
        // itself (the OpenAI SDK it constructs joins the resource path onto
        // the base URL), so an endpoint that already ends in the segment
        // would be doubled. Adapter-owned and protocol-gated.
        emission = EmissionMerge.DropResponsesSegment(emission);

        // One shared mapping: the emission carries this protocol's
        // env-var names and values, merged by the one consumption rule.
        Dictionary<string, string> envVars = new(StringComparer.Ordinal);
        EmissionMerge.MergeInto(envVars, emission, request.ApiKey);

        List<string> args = [];
        if (model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(model);
        }

        if (printPrompt is not null)
        {
            // -p is pi's boolean print-mode flag and the prompt is the
            // message positional after it. Both ride ArgumentList so the
            // prompt stays one argv element whatever it contains (and a
            // non-empty list makes the process library ignore Arguments).
            args.Add("-p");
            args.Add(printPrompt);
        }

        ProcessConfiguration config = new("pi")
        {
            ArgumentList = args,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return new LaunchPlan(config, []);
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