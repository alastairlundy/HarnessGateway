using System.Text.Json;
using CliInvoke;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Serialization;

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
        ProtocolKind protocol = request.Launch switch
        {
            LaunchResolution.ProviderCase providerCase => providerCase.Protocol,
            LaunchResolution.AdHocCase adHoc => adHoc.Protocol,
            _ => throw new InvalidOperationException(
                $"unexpected launch resolution case '{request.Launch.GetType().Name}'"),
        };

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

        // Kilo's built-in provider registry only accepts --model values of
        // the form provider_id/model_id, and the model must exist in that
        // provider's list - a bare id is rejected as `Model not found:
        // <id>/`, and a catalog provider id paired with a model Kilo does
        // not know is rejected too. The launch therefore registers the
        // model under the protocol's own provider id through
        // KILO_CONFIG_CONTENT (Kilo, an OpenCode fork, honours the inline
        // config env var and treats it as trusted config, so {env:...}
        // key references resolve) and passes the prefixed id on --model.
        // Kilo's documented provider vocabulary: `openai-compatible` is
        // the OpenAI Chat Completions-compatible custom endpoint,
        // `openai` speaks the Responses API, `anthropic` the Messages API.
        string? modelRef = null;
        string? providerId = protocol switch
        {
            ProtocolKind.OpenAiChat => "openai-compatible",
            ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => null,
        };
        if (model is { Length: > 0 } && providerId is not null
            && !model.Contains('/', StringComparison.Ordinal))
        {
            // The registry reference Kilo's --model flag resolves; the raw
            // model id stays the models key the session config registers.
            modelRef = $"{providerId}/{model}";
        }
        else if (model is { Length: > 0 })
        {
            modelRef = model;
        }

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

        // The model registration rides KILO_CONFIG_CONTENT, the inline
        // session-config env var Kilo loads between global and project
        // configuration - it never touches a file on disk and is treated as
        // trusted config, so the {env:...} key reference resolves. The
        // launch-scoped endpoint override replaces the provider's own base
        // URL there, and the model entry registers the launch's model id so
        // --model provider_id/model_id resolves whatever Kilo's bundled
        // catalog knows. `kilo run` reads the same configuration, so the
        // one-shot plan sets the same variable.
        if (providerId is not null && model is { Length: > 0 })
        {
            string configContent = BuildSessionConfig(providerId, emission.BaseUrlValue, model);
            envVars["KILO_CONFIG_CONTENT"] = configContent;
        }

        ProcessConfiguration config = new("kilo")
        {
            ArgumentList = BuildArguments(printPrompt, modelRef),
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return new LaunchPlan(config, []);
    }

    /// <summary>
    /// Builds the inline session config (KILO_CONFIG_CONTENT): the model
    /// selection plus the provider entry that carries the per-launch endpoint
    /// override and registers the model id, since Kilo rejects a --model
    /// reference to a model its bundled catalog does not list. String values
    /// are escaped with the runtime JSON encoder; nothing else in the payload
    /// is caller-controlled, so composition stays a fixed shape.
    /// </summary>
    private static string BuildSessionConfig(string providerId, string baseUrl, string model)
    {
        string apiKeyRef = string.Equals(providerId, "anthropic", StringComparison.OrdinalIgnoreCase)
            ? "{env:ANTHROPIC_API_KEY}"
            : "{env:OPENAI_API_KEY}";

        string providerJson = JsonSerializer.Serialize(providerId, StringJsonContext.Default.String);
        string baseUrlJson = JsonSerializer.Serialize(baseUrl, StringJsonContext.Default.String);
        string apiKeyJson = JsonSerializer.Serialize(apiKeyRef, StringJsonContext.Default.String);
        string modelIdJson = JsonSerializer.Serialize($"{providerId}/{model}", StringJsonContext.Default.String);
        string modelJson = JsonSerializer.Serialize(model, StringJsonContext.Default.String);

        // Plain concatenation: a raw interpolated string literal fights the
        // payload's literal brace pairs ({"name":...}}), and every controlled
        // value is already escaped above.
        return "{\"$schema\":\"https://app.kilo.ai/config.json\",\"model\":" + modelIdJson
                                                                             + ",\"provider\":{" + providerJson
                                                                             + ":{\"options\":{\"baseURL\":" + baseUrlJson
                                                                             + ",\"apiKey\":" + apiKeyJson
                                                                             + "},\"models\":{" + modelJson
                                                                             + ":{\"name\":" + modelJson + "}}}}}";
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