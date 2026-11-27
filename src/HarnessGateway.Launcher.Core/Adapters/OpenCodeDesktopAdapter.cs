using System.Text.Json;
using CliInvoke;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Serialization;

namespace HarnessGateway.Launcher.Core.Adapters;

/// <summary>
/// OpenCode Desktop - the vendor's Electron desktop app, and the first
/// Kind=Gui adapter. Every value below is researched vendor data rather than
/// launcher convention; the fact list each declaration traces back to:
/// </summary>
/// <remarks>
/// <para>
/// Identity: the prod channel ships productName "OpenCode" with appId
/// "ai.opencode.desktop" (packages/desktop/electron-builder.config.ts in
/// anomalyco/opencode), so the Windows image name is OpenCode.exe, the macOS
/// bundle is OpenCode.app with the same-named executable inside it (the
/// Homebrew cask Casks/o/opencode-desktop.rb installs app "OpenCode.app"),
/// and the Linux deb/rpm executable is ai.opencode.desktop - linux.executableName
/// is the appId, the packaged desktop entry runs
/// /opt/OpenCode/ai.opencode.desktop (packages/desktop/resources/linux/), and
/// electron-builder's after-install template symlinks that into /usr/bin.
/// The lowercase "opencode" token stays with the CLI sibling
/// (<see cref="OpenCodeAdapter"/>): casing keeps the two apart on a
/// case-sensitive filesystem, while on Windows the two names compare equal
/// case-insensitively, so discovery and detection cannot tell them apart
/// there - a collision the launcher knowingly accepts rather than papering
/// over.
/// </para>
/// <para>
/// Surfaces: the app forks its sidecar server with a copy of its own
/// environment (packages/desktop/src/main/server.ts, createSidecarEnv) and the
/// sidecar runs opencode's embedded server rather than a CLI
/// (packages/desktop/src/main/sidecar.ts, Server.listen), whose config loader
/// reads OPENCODE_CONFIG / OPENCODE_CONFIG_CONTENT / OPENCODE_CONFIG_DIR plus
/// the provider variables out of that environment
/// (packages/core/src/flag/flag.ts) - environment is a real surface. The same
/// loader reads opencode.jsonc | opencode.json | config.json under the XDG
/// config directory and a project opencode.json
/// (packages/opencode/src/config/config.ts, packages/core/src/global.ts), so
/// files are a real surface too. Flags are not: the embedded server parses no
/// command line, and the switches Electron does accept are runtime switches,
/// not backend configuration. One caveat on the environment surface:
/// preferAppEnv merges the login shell's environment over the app's own
/// (packages/desktop/src/main/server.ts), so a same-named variable exported by
/// the shell outranks the one this launch hands over.
/// </para>
/// <para>
/// Config files: the app chdirs to the home directory at startup
/// (packages/desktop/src/main/index.ts), so the launcher's working directory
/// does not select the project config the sidecar reads.
/// </para>
/// </remarks>
public sealed class OpenCodeDesktopAdapter : IHarnessAdapter
{
    /// <summary>
    /// The macOS start target: the executable inside the bundle discovery
    /// matches under /Applications. Launching the inner binary instead of
    /// `open -a` keeps the environment surface working - LaunchServices
    /// starts an app with a fresh environment, so the endpoint, key, and model
    /// handed over here would never reach the sidecar through `open`.
    /// </summary>
    private const string MacosLaunchTarget =
        "/Applications/OpenCode.app/Contents/MacOS/OpenCode";

    /// <summary>
    /// The Windows start target, verified against a live install (and the
    /// same location the vendor's own issue tracker quotes): the NSIS
    /// installer lays this build down per-user, under a directory name no
    /// repository configuration declares and no discovery root scans. Used
    /// only when it exists - otherwise <see cref="LaunchTarget"/> falls back
    /// to the bare name, which resolves case-insensitively on Windows and can
    /// hand back the CLI sibling's binary instead of this app.
    /// </summary>
    private static readonly string WindowsLaunchTarget = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "@opencode-aidesktop",
        "OpenCode.exe");

    public HarnessMetadata Metadata { get; } = new(
        Name: "OpenCode Desktop",
        CommandName: "opencode-desktop",
        [],
        Kind: HarnessKind.Gui,
        RequiresModel: true,
        InstallHint: "Install via: brew install --cask opencode-desktop or download from https://opencode.ai/download",
        DetectionCommand:
        "dir %LOCALAPPDATA%\\Programs\\@opencode-aidesktop (Windows), ls /Applications/OpenCode.app (macOS), which ai.opencode.desktop (Linux)",
        // Project config (the folder open in the app) and the global config
        // under the XDG config directory. The loader prefers opencode.jsonc,
        // then opencode.json, then config.json in the global directory, so a
        // future writer has to honour that order instead of assuming .json -
        // this adapter writes neither today.
        ["opencode.json", "~/.config/opencode/opencode.json"],
        new HashSet<ProtocolKind>
        {
            ProtocolKind.OpenAiChat,
            ProtocolKind.OpenAiResponses,
            ProtocolKind.Anthropic,
        },
        new HashSet<ConfigurationSurface>
        {
            ConfigurationSurface.Environment,
            ConfigurationSurface.Files,
        },
        // Declared-name order is discovery's first-match order: the Windows
        // image name, the macOS bundle and the process name it runs as, then
        // the Linux executable.
        ["OpenCode.exe", "OpenCode.app", "OpenCode", "ai.opencode.desktop"]);

    private readonly IGuiInstallDiscovery _discovery;

    public OpenCodeDesktopAdapter()
        : this(new GuiInstallDiscovery(new FilePathResolver()))
    {
    }

    internal OpenCodeDesktopAdapter(IGuiInstallDiscovery discovery)
    {
        _discovery = discovery;
    }

    /// <summary>
    /// The launch flow never reads this member for a Kind=Gui harness - it
    /// asks install discovery itself - but the wizard has no discovery
    /// collaborator of its own and probes every adapter through here before
    /// it will offer one. So this delegates to the same discovery the
    /// flow runs over the same declared names: one implementation, one
    /// answer, and no way for "offered by the wizard" and "startable by the
    /// launch" to drift apart.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_discovery.Discover(Metadata.InstanceProcessNames).Found);

    /// <summary>
    /// Plans the launch through the environment surface only: the endpoint,
    /// the key (when one was saved - never a placeholder, never a write), and
    /// the inline session config carrying the model, all on the child process
    /// this plan starts. No <see cref="ConfigurationAction"/> is planned, so
    /// there is nothing on disk for the flow to back up, restore, or leak.
    /// </summary>
    public Task<LaunchPlan> PlanLaunchAsync(ResolvedLaunch request, CancellationToken cancellationToken = default)
    {
        // Direct case branching: same family handoff as the CLI
        // sibling - each case carries its own effective protocol, base URL,
        // and model, and no protocol is computed here.
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

        // Same engine as the CLI sibling: the AI SDK behind the desktop app's
        // embedded opencode server posts Responses-API requests to
        // {baseURL}/responses itself, so an endpoint that already ends in
        // the segment would be doubled. Adapter-owned and protocol-gated.
        emission = EmissionMerge.DropResponsesSegment(emission);

        // One shared mapping: the emission carries this protocol's
        // env-var names and values, merged by the one consumption rule.
        Dictionary<string, string?> envVars = new(StringComparer.Ordinal);
        EmissionMerge.MergeInto(envVars, emission, request.ApiKey);

        string? providerId = emission.Protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => "openai",
            ProtocolKind.Anthropic => "anthropic",
            _ => null,
        };

        // Same channel the CLI sibling uses: the desktop app takes no --model
        // flag of its own (it is not a CLI), so the model goes into the inline
        // config environment variable the embedded server loads, and the
        // provider options reference the key by env name so the secret exists
        // in exactly one place - the child's environment.
        if (providerId is not null && model is { Length: > 0 })
        {
            string configContent = BuildSessionConfig(providerId, emission.BaseUrlValue, model);
            envVars["OPENCODE_CONFIG_CONTENT"] = configContent;
        }

        ProcessConfiguration config = new(LaunchTarget())
        {
            Arguments = string.Empty,
            WorkingDirectoryPath = request.WorkingDirectory,
            OutputRedirection = true,
            EnvironmentVariables = envVars,
        };

        return Task.FromResult(new LaunchPlan(config, []));
    }

    /// <summary>
    /// The file this adapter starts. Availability stays discovery's answer;
    /// this only says what to run once discovery says yes - and on two of the
    /// three platforms the file that starts is not the file a bare-name lookup
    /// would return (see <see cref="MacosLaunchTarget"/> and
    /// <see cref="WindowsLaunchTarget"/>). Linux takes the packaged name,
    /// which is where the packaging symlink puts it.
    /// </summary>
    private static string LaunchTarget()
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacosLaunchTarget;
        }

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(WindowsLaunchTarget) ? WindowsLaunchTarget : "OpenCode.exe";
        }

        return "ai.opencode.desktop";
    }

    /// <summary>
    /// The inline session config (OPENCODE_CONFIG_CONTENT): the model
    /// selection plus the provider entry that carries the per-launch endpoint
    /// override and registers the model id, since custom gateway endpoints are
    /// not in opencode's bundled model registry. Mirrors the CLI sibling's
    /// payload - each adapter carries its own copy the way the detection probe
    /// is carried - and every controlled value is serialized rather than
    /// interpolated, so composition stays a fixed shape.
    /// </summary>
    private static string BuildSessionConfig(string providerId, string baseUrl, string model)
    {
        string apiKeyRef = string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase)
            ? "{env:OPENAI_API_KEY}"
            : "{env:ANTHROPIC_API_KEY}";

        string providerJson = JsonSerializer.Serialize(providerId, StringJsonContext.Default.String);
        string baseUrlJson = JsonSerializer.Serialize(baseUrl, StringJsonContext.Default.String);
        string apiKeyJson = JsonSerializer.Serialize(apiKeyRef, StringJsonContext.Default.String);
        string modelIdJson = JsonSerializer.Serialize($"{providerId}/{model}", StringJsonContext.Default.String);
        string modelJson = JsonSerializer.Serialize(model, StringJsonContext.Default.String);

        return "{\"$schema\":\"https://opencode.ai/config.json\",\"model\":" + modelIdJson
                                                                             + ",\"provider\":{" + providerJson
                                                                             + ":{\"options\":{\"baseURL\":" + baseUrlJson
                                                                             + ",\"apiKey\":" + apiKeyJson
                                                                             + "},\"models\":{" + modelJson
                                                                             + ":{\"name\":" + modelJson + "}}}}}";
    }
}
