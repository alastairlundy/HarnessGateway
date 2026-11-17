using CliInvoke;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Toml;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class CodexAdapter : IHarnessAdapter
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

    public async Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
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
        };

        return new LaunchPlan(config, actions);
    }

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
