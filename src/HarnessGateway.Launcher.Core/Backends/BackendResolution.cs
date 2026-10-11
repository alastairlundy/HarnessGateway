using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Text;

namespace HarnessGateway.Launcher.Core.Backends;

/// <summary>
/// Profile-aware backend resolution shared by the launch path and the
/// config/wizard read paths: named-entry selection, the strict
/// <c>DefaultBackend</c> pointer read (no first-entry fallback, no
/// auto-promotion), and field-by-field override layering. Protocol selection
/// is always explicit - nothing here probes endpoints.
/// </summary>
public static class BackendResolution
{
    private static readonly string[] ProtocolNames =
        ["openai-chat", "openai-responses", "anthropic"];

    /// <summary>
    /// Resolve the backend for a launch: select a saved entry (by name when
    /// <paramref name="backendName"/> is given, otherwise via the strict
    /// default-pointer read), then layer the per-launch overrides on top of it
    /// field by field. Flag values win over the entry per field; keys are never
    /// overridable - the entry's stored key is the sole credential source.
    /// </summary>
    public static BackendResolutionResult Resolve(
        LauncherSettings settings,
        string? backendName,
        string? endpoint,
        string? protocol,
        string? model)
    {
        // Raw-URL rule: --endpoint and --protocol travel together.
        if (endpoint is not null && protocol is null)
        {
            return Fail(
                BackendResolutionFailure.EndpointWithoutProtocol,
                "--endpoint requires --protocol to be specified");
        }

        if (protocol is not null && endpoint is null)
        {
            return Fail(
                BackendResolutionFailure.ProtocolWithoutEndpoint,
                "--protocol requires --endpoint to be specified");
        }

        Uri? parsedEndpoint = null;
        ProtocolKind? parsedProtocol = null;

        if (endpoint is not null && protocol is not null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            {
                return Fail(
                    BackendResolutionFailure.InvalidEndpoint,
                    $"Invalid endpoint '{endpoint}': must be an absolute URL.");
            }

            ProtocolKind? kind = TryParseProtocol(protocol);
            if (kind is null)
            {
                return Fail(
                    BackendResolutionFailure.InvalidProtocol,
                    $"Invalid protocol '{protocol}'. Must be one of: {string.Join(", ", ProtocolNames)}");
            }

            parsedEndpoint = uri;
            parsedProtocol = kind;
        }

        // Select the entry: --backend resolves against the saved list; without
        // it the DefaultBackend pointer is the only source of truth.
        Backend? entry;
        if (backendName is not null)
        {
            entry = FindByName(settings, backendName);
            if (entry is null)
            {
                return Fail(
                    BackendResolutionFailure.UnknownBackendName,
                    UnknownNameMessage(settings, backendName));
            }
        }
        else if (!string.IsNullOrEmpty(settings.DefaultBackend))
        {
            entry = FindByName(settings, settings.DefaultBackend);
            if (entry is null)
            {
                return Fail(
                    BackendResolutionFailure.DanglingDefaultBackend,
                    $"unknown backend name '{settings.DefaultBackend}' in the default backend pointer. " +
                    "Run 'harnessgateway config set defaultBackend <name>' to choose a default backend.");
            }
        }
        else if (settings.Backends.Count > 0)
        {
            return Fail(
                BackendResolutionFailure.AbsentDefaultBackend,
                "no default backend is set while saved backend entries exist. " +
                "Run 'harnessgateway config set defaultBackend <name>' to choose a default backend.");
        }
        else
        {
            entry = null;
        }

        if (entry is null)
        {
            // Nothing stored to layer onto: a complete raw URL still launches
            // as an ad-hoc override entry; otherwise there is no backend at all.
            if (parsedEndpoint is not null && parsedProtocol is not null)
            {
                return Success(new Backend
                {
                    Name = "override",
                    BaseUri = parsedEndpoint,
                    Protocol = parsedProtocol.Value,
                    DefaultModel = model,
                });
            }

            return Success(backend: null);
        }

        Backend resolved = entry;
        if (parsedEndpoint is not null)
        {
            resolved = resolved with { BaseUri = parsedEndpoint };
        }

        if (parsedProtocol is not null)
        {
            resolved = resolved with { Protocol = parsedProtocol.Value };
        }

        if (model is not null)
        {
            resolved = resolved with { DefaultModel = model };
        }

        return Success(resolved);
    }

    public static bool IsValidProtocol(string? value) =>
        value is not null && TryParseProtocol(value) is not null;

    public static ProtocolKind? TryParseProtocol(string value) => value switch
    {
        "openai-chat" => ProtocolKind.OpenAiChat,
        "openai-responses" => ProtocolKind.OpenAiResponses,
        "anthropic" => ProtocolKind.Anthropic,
        _ => null,
    };

    public static string ProtocolName(ProtocolKind protocol) => protocol switch
    {
        ProtocolKind.OpenAiChat => "openai-chat",
        ProtocolKind.OpenAiResponses => "openai-responses",
        ProtocolKind.Anthropic => "anthropic",
        _ => protocol.ToString(),
    };

    private static Backend? FindByName(LauncherSettings settings, string name) =>
        settings.Backends.FirstOrDefault(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string UnknownNameMessage(LauncherSettings settings, string name)
    {
        string message = $"unknown backend name '{name}'.";

        // Best-effort close-match hint; non-contractual UX that may be
        // improved or dropped without changing the resolution outcome.
        string? hint = CloseMatch.Best(settings.Backends.Select(b => b.Name), name);
        if (hint is not null)
        {
            message += $" Did you mean '{hint}'?";
        }

        return message;
    }

    private static BackendResolutionResult Fail(BackendResolutionFailure failure, string message) =>
        new() { Failure = failure, Message = message };

    private static BackendResolutionResult Success(Backend? backend) =>
        new() { Backend = backend };
}