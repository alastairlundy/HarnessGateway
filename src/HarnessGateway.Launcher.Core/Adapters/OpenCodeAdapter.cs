using System.Text.Json;
using CliInvoke;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Core.Adapters;

public sealed class OpenCodeAdapter : IHarnessAdapter
{
    public HarnessMetadata Metadata { get; } = new(
        Name: "OpenCode",
        CommandName: "opencode",
        [],
        Kind: HarnessKind.Cli,
        RequiresModel: true,
        InstallHint: "Install via: npm install -g @opencode/cli",
        DetectionCommand: "opencode --version",
        ["opencode.json"],
        new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic,
        },
        new HashSet<ConfigurationSurface>(),
        []);

    private readonly Func<string, CancellationToken, Task<bool>> _detectCommand;

    public OpenCodeAdapter()
        : this(DetectOnPath)
    {
    }

    internal OpenCodeAdapter(Func<string, CancellationToken, Task<bool>> detectCommand)
    {
        _detectCommand = detectCommand;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _detectCommand("opencode", cancellationToken);

    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> envVars = new(StringComparer.Ordinal);

        string baseUrl = request.Backend.BaseUri.ToString().TrimEnd('/');

        string? providerId = request.Backend.Protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => null,
        };

        switch (request.Backend.Protocol)
        {
            case ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses:
                envVars["OPENAI_BASE_URL"] = baseUrl;
                if (request.Backend.ApiKey is { Length: > 0 })
                    envVars["OPENAI_API_KEY"] = request.Backend.ApiKey;
                break;
            case ProtocolKind.Anthropic:
                envVars["ANTHROPIC_BASE_URL"] = baseUrl;
                if (request.Backend.ApiKey is { Length: > 0 })
                    envVars["ANTHROPIC_API_KEY"] = request.Backend.ApiKey;
                break;
        }

        // opencode v2 has no top-level --model flag: `opencode --model <id>`
        // is rejected outright ("Unrecognized flag: --model", exit 1), so the
        // model cannot ride on argv for the TUI launch. It reaches the
        // session through the inline-config env var instead, which opencode
        // loads between global and project configuration and which never
        // touches a file on disk. Provider options use {env:...} references
        // rather than literals - the key never gets duplicated into a second
        // channel, and the TUI prompts for auth when no key is set.
        if (providerId is not null && request.Model is { Length: > 0 })
        {
            string configContent = BuildSessionConfig(providerId, baseUrl, request.Model);
            envVars["OPENCODE_CONFIG_CONTENT"] = configContent;
        }

        ProcessConfiguration config = new("opencode")
        {
            Arguments = "",
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return Task.FromResult(new LaunchPlan(config, []));
    }

    /// <summary>
    /// Builds the inline session config (OPENCODE_CONFIG_CONTENT): the model
    /// selection plus the provider entry that carries the per-launch endpoint
    /// override and registers the model id, since custom gateway endpoints
    /// are not in opencode's bundled model registry. String values are
    /// escaped with the runtime JSON encoder; nothing else in the payload is
    /// caller-controlled, so composition stays a fixed shape.
    /// </summary>
    private static string BuildSessionConfig(string providerId, string baseUrl, string model)
    {
        string apiKeyRef = string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase)
            ? "{env:OPENAI_API_KEY}"
            : "{env:ANTHROPIC_API_KEY}";

        string providerJson = JsonSerializer.Serialize(providerId);
        string baseUrlJson = JsonSerializer.Serialize(baseUrl);
        string apiKeyJson = JsonSerializer.Serialize(apiKeyRef);
        string modelIdJson = JsonSerializer.Serialize($"{providerId}/{model}");
        string modelJson = JsonSerializer.Serialize(model);

        // Plain concatenation: a raw interpolated string literal fights the
        // payload's literal brace pairs ({"name":...}}), and every controlled
        // value is already escaped above.
        return "{\"$schema\":\"https://opencode.ai/config.json\",\"model\":" + modelIdJson
                                                                             + ",\"provider\":{" + providerJson
                                                                             + ":{\"options\":{\"baseURL\":" + baseUrlJson
                                                                             + ",\"apiKey\":" + apiKeyJson
                                                                             + "},\"models\":{" + modelJson
                                                                             + ":{\"name\":" + modelJson + "}}}}}";
    }

    private static async Task<bool> DetectOnPath(string command, CancellationToken cancellationToken)
    {
        try
        {
            string fileName = OperatingSystem.IsWindows() ? "where" : "which";
            ProcessConfiguration config = new (fileName, command)
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
