using CliInvoke;
using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Backends;
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

    private readonly Func<string, CancellationToken, Task<string?>> _detectCommand;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public CodexAdapter()
        : this(DetectOnPath, null)
    {
    }

    internal CodexAdapter(
        Func<string, CancellationToken, Task<string?>> detectCommand,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _detectCommand = detectCommand;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        Metadata = new HarnessMetadata(
            Name: "codex",
            Kind: HarnessKind.Cli,
            RequiresModel: true,
            InstallHint: "Install via: npm i -g @openai/codex or brew install codex",
            DetectionCommand: "codex --version",
            ConfigFileCandidates: [ResolveConfigPath(_getEnvironmentVariable)],
            SupportedProtocols: new HashSet<ProtocolKind>
            {
                ProtocolKind.OpenAiResponses
            });
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await _detectCommand("codex", cancellationToken);
        return result is not null;
    }

    public async Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var configPath = ResolveConfigPath(_getEnvironmentVariable);
        var existing = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : string.Empty;

        var editor = TomlDocumentEditor.Parse(existing);
        if (editor.BlockExists(ProviderBlockName))
        {
            // T037: refuse before any write - never adopt or overwrite a user-authored block.
            throw new InvalidOperationException(
                $"Codex config '{configPath}' already contains a [{ProviderBlockName}] provider block. " +
                "The launcher never adopts or overwrites a user-authored provider block - " +
                "remove or rename that block and launch again.");
        }

        editor.UpsertBlock(ProviderBlockName, new Dictionary<string, object?>
        {
            ["name"] = "HarnessGateway",
            ["base_url"] = request.Backend.BaseUri.ToString().TrimEnd('/'),
            ["env_key"] = EnvKeyName,
            ["wire_api"] = "responses"
        });
        editor.SetTopLevelValue("model_provider", ProviderId);
        if (request.Model is { Length: > 0 })
        {
            editor.SetTopLevelValue("model", request.Model);
        }

        var actions = new List<ConfigurationAction>
        {
            new ConfigFileWriteAction(configPath, editor.ToToml())
        };

        // D012: inject the saved key only when one exists; never a dummy value.
        if (request.Backend.ApiKey is { Length: > 0 })
        {
            actions.Add(new EnvironmentVariableAction(EnvKeyName, request.Backend.ApiKey));
        }

        var config = new ProcessConfiguration("codex")
        {
            Arguments = string.Empty,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true
        };

        return new LaunchPlan(config, actions);
    }

    /// <summary>
    /// Resolves the real Codex config file: a user-set <c>CODEX_HOME</c> wins, otherwise
    /// the file falls back to <c>~/.codex/config.toml</c>.
    /// </summary>
    private static string ResolveConfigPath(Func<string, string?> getEnvironmentVariable)
    {
        var codexHome = getEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "config.toml");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
    }

    private static async Task<string?> DetectOnPath(string command, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = OperatingSystem.IsWindows() ? "where" : "which";
            var config = new ProcessConfiguration(fileName, command)
            {
                OutputRedirection = true
            };

            var result = await CliRun.RunAsync(config, cancellationToken: cancellationToken);
            return result.ExitCode == 0 ? command : null;
        }
        catch
        {
            return null;
        }
    }
}
