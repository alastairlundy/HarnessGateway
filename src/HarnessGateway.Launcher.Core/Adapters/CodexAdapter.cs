using CliInvoke;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Toml;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class CodexAdapter : IHarnessAdapter, INonInteractiveLaunchAdapter
{
    internal const string ProviderId = "harnessgateway";
    internal const string ProviderBlockName = $"model_providers.{ProviderId}";
    internal const string EnvKeyName = "HARNESSGATEWAY_API_KEY";

    public HarnessMetadata Metadata { get; }

    private readonly Func<string, CancellationToken, Task<bool>> _detectCommand;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public CodexAdapter()
        : this(DetectOnPath, getEnvironmentVariable: null)
    {
    }

    internal CodexAdapter(
        Func<string, CancellationToken, Task<bool>> detectCommand,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _detectCommand = detectCommand;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        Metadata = new HarnessMetadata(
            Name: "codex",
            CommandName: "codex",
            ["codex-cli"],
            Kind: HarnessKind.Cli,
            RequiresModel: true,
            InstallHint: "Install via: npm i -g @openai/codex or brew install codex",
            DetectionCommand: "codex --version",
            [ResolveConfigPath(_getEnvironmentVariable)],
            new HashSet<ProtocolKind>
            {
                ProtocolKind.OpenAiResponses,
            },
            new HashSet<ConfigurationSurface>(),
            []);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("codex", cancellationToken);

    public async Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default) =>
        await BuildPlanAsync(request, printPrompt: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The one-shot mode: <c>codex exec "&lt;prompt&gt;"</c> - the
    /// harness's own "Run Codex non-interactively" subcommand. The config
    /// write and environment plan exactly as the interactive launch does;
    /// only argv differs.
    /// </summary>
    public async Task<LaunchPlan> PlanNonInteractiveAsync(
        ResolvedLaunch request,
        string prompt,
        CancellationToken cancellationToken = default) =>
        await BuildPlanAsync(request, prompt, cancellationToken).ConfigureAwait(false);

    private async Task<LaunchPlan> BuildPlanAsync(
        ResolvedLaunch request,
        string? printPrompt,
        CancellationToken cancellationToken)
    {
        (ProtocolEmission emission, string? model) = ResolveEmission(request);

        // Codex joins the provider-relative "/responses" path onto base_url
        // itself (this adapter writes wire_api = "responses" below), so an
        // endpoint that already ends in the segment would be doubled. The
        // drop is adapter-owned - codex is the harness whose own config
        // guarantees the appending - and the trimmed value feeds the config
        // block and the process environment alike.
        emission = EmissionMerge.DropResponsesSegment(emission);

        string configPath = ResolveConfigPath(_getEnvironmentVariable);
        string existing = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
.ConfigureAwait(false) : string.Empty;

        // One shared mapping: the emission's base URL - already
        // slash-trimmed by the planner - feeds the harness-owned config
        // block and the process environment alike.
        TomlDocumentEditor editor = ApplyProviderConfig(
            existing, configPath, emission.BaseUrlValue, model);

        // The emission's base-url variable rides the spawned process
        // environment too, so every launch carries exactly one base-url
        // variable. The key slot's canonical name is not this
        // harness's: codex reads the variable its config block names
        // (env_key), so the secret lands there and only there - never a
        // dummy value, never a second copy under another name.
        Dictionary<string, string> envVars = new(StringComparer.Ordinal)
        {
            [emission.BaseUrlVariable] = emission.BaseUrlValue,
        };
        if (request.ApiKey is { Length: > 0 })
        {
            envVars[EnvKeyName] = request.ApiKey;
        }

        List<ConfigurationAction> actions =
        [
            new ConfigFileWriteAction(configPath, editor.ToToml()),
        ];

        ProcessConfiguration config = new("codex")
        {
            Arguments = string.Empty,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
            // `codex exec [PROMPT]` is the one-shot subcommand; the prompt
            // rides ArgumentList so it stays one argv element. When
            // ArgumentList is non-empty the process library ignores the
            // Arguments string - which `exec` wants. An empty list (the
            // interactive launch) is the property's default.
            ArgumentList = printPrompt is null ? [] : ["exec", "--skip-git-repo-check", printPrompt],
        };

        // The interactive launch additionally plans a daemon stop; the
        // reasoning lives with the helper it calls.
        return new LaunchPlan(config, actions, BuildDaemonStopProcess(request, printPrompt));
    }

    /// <summary>
    /// The interactive-only pre-launch step: <c>codex app-server daemon
    /// stop</c>, so the TUI attaches to a daemon this launch spawned (and
    /// gave its key to) instead of a shared one that predates it. Null for
    /// a print launch, which runs embedded and never consults a daemon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why the TUI needs this and <c>codex exec</c> does not: the TUI is a
    /// client of codex's managed app-server daemon, and when one is already
    /// running - started by any earlier codex session - it attaches to that
    /// rather than starting its own. A daemon's environment was frozen when
    /// it spawned, so the key this launch injects into its child never
    /// reaches the shared one and every request fails with "Missing
    /// environment variable: HARNESSGATEWAY_API_KEY" while the identical
    /// exec launch succeeds. Stopping it first makes the TUI spawn a fresh
    /// daemon that inherits this launch's environment.
    /// </para>
    /// <para>
    /// Best effort by contract: codex reports <c>notRunning</c> and exits 0
    /// when there is nothing to stop, and the runner bounds the call so a
    /// wedged daemon cannot hold the launch hostage.
    /// </para>
    /// </remarks>
    private static ProcessConfiguration? BuildDaemonStopProcess(
        ResolvedLaunch request,
        string? printPrompt)
    {
        if (printPrompt is not null)
        {
            return null;
        }

        return new ProcessConfiguration("codex")
        {
            ArgumentList = ["app-server", "daemon", "stop"],
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
        };
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

    /// <summary>
    /// Parses the existing config, refuses a user-authored harnessgateway
    /// block before any write, and applies the provider block plus the
    /// top-level model keys. The refusal is the adapter's own config shape;
    /// only the base-url value comes from the shared mapping.
    /// </summary>
    private static TomlDocumentEditor ApplyProviderConfig(
        string existing,
        string configPath,
        string baseUrl,
        string? model)
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(existing);
        if (editor.NameInUse(ProviderBlockName))
        {
            // Refuse before any write - never adopt or overwrite a user-authored block.
            throw new LaunchRefusalException(
                $"Refusing to launch codex: '{configPath}' already defines a '{ProviderBlockName}' provider. " +
                "The launcher writes its own provider under the id 'harnessgateway' and never adopts or " +
                "overwrites one you authored - remove or rename that provider block " +
                "(or point CODEX_HOME at a different config) and launch again.");
        }

        editor.UpsertBlock(ProviderBlockName, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "HarnessGateway",
            ["base_url"] = baseUrl,
            ["env_key"] = EnvKeyName,
            ["wire_api"] = "responses",
            ["requires_openai_auth"] = false,
        });
        editor.SetTopLevelValue("model_provider", ProviderId);
        if (model is { Length: > 0 })
        {
            editor.SetTopLevelValue("model", model);
        }

        return editor;
    }

    /// <summary>
    /// Resolves the real Codex config file: a user-set <c>CODEX_HOME</c> wins, otherwise
    /// the file falls back to <c>~/.codex/config.toml</c>.
    /// </summary>
    private static string ResolveConfigPath(Func<string, string?> getEnvironmentVariable)
    {
        string? codexHome = getEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "config.toml");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
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
