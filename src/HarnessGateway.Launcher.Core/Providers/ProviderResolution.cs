using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Text;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Profile-aware backend resolution shared by the launch path and the
/// config/wizard read paths: named-entry selection, the strict
/// <c>DefaultProvider</c> pointer read (no first-entry fallback, no
/// auto-promotion), and field-by-field override layering. DefaultProtocol selection
/// is always explicit - nothing here probes endpoints.
/// </summary>
public static class ProviderResolution
{
    private static readonly string[] ProtocolNames =
        ["openai-chat", "openai-responses", "anthropic"];

    /// <summary>
    /// Resolve the backend for a launch: select a saved entry (by name when
    /// <paramref name="providerName"/> is given, otherwise via the strict
    /// default-pointer read), then layer the per-launch overrides on top of it
    /// field by field. Flag values win over the entry per field; keys are never
    /// overridable - the entry's stored key is the sole credential source.
    /// </summary>
    public static ProviderResolutionResult Resolve(
        LauncherSettings settings,
        string? providerName,
        string? endpoint,
        string? protocol,
        string? model)
    {
        // Raw-URL rule: --endpoint and --protocol travel together.
        if (endpoint is not null && protocol is null)
        {
            return Fail(
                ProviderResolutionFailure.EndpointWithoutProtocol,
                "--endpoint requires --protocol to be specified");
        }

        if (protocol is not null && endpoint is null)
        {
            return Fail(
                ProviderResolutionFailure.ProtocolWithoutEndpoint,
                "--protocol requires --endpoint to be specified");
        }

        Uri? parsedEndpoint = null;
        ProtocolKind? parsedProtocol = null;

        if (endpoint is not null && protocol is not null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            {
                return Fail(
                    ProviderResolutionFailure.InvalidEndpoint,
                    $"Invalid endpoint '{endpoint}': must be an absolute URL.");
            }

            ProtocolKind? kind = TryParseProtocol(protocol);
            if (kind is null)
            {
                return Fail(
                    ProviderResolutionFailure.InvalidProtocol,
                    $"Invalid protocol '{protocol}'. Must be one of: {string.Join(", ", ProtocolNames)}");
            }

            parsedEndpoint = uri;
            parsedProtocol = kind;
        }

        // Select the entry: --backend resolves against the saved list; without
        // it the DefaultProvider pointer is the only source of truth.
        Provider? entry;
        if (providerName is not null)
        {
            Result<Provider> named = FindByName(settings, providerName);
            if (!named.Success)
            {
                return Fail(
                    ProviderResolutionFailure.UnknownProviderName,
                    UnknownNameMessage(settings, providerName));
            }

            entry = named.Value;
        }
        else if (!string.IsNullOrEmpty(settings.DefaultProvider))
        {
            Result<Provider> defaultEntry = FindByName(settings, settings.DefaultProvider);
            if (!defaultEntry.Success)
            {
                return Fail(
                    ProviderResolutionFailure.DanglingDefaultProvider,
                    $"unknown backend name '{settings.DefaultProvider}' in the default backend pointer. " +
                    "Run 'harnessgateway config set defaultBackend <name>' to choose a default backend.");
            }

            entry = defaultEntry.Value;
        }
        else if (settings.Providers.Count > 0)
        {
            return Fail(
                ProviderResolutionFailure.AbsentDefaultProvider,
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
                return new ProviderResolutionResult.Resolved(new Provider
                {
                    Name = "override",
                    BaseUri = parsedEndpoint,
                    DefaultProtocol = parsedProtocol.Value,
                    DefaultModel = model,
                });
            }

            return new ProviderResolutionResult.EmptySettings();
        }

        Provider resolved = entry;
        if (parsedEndpoint is not null)
        {
            resolved = resolved with { BaseUri = parsedEndpoint };
        }

        if (parsedProtocol is not null)
        {
            resolved = resolved with { DefaultProtocol = parsedProtocol.Value };
        }

        if (model is not null)
        {
            resolved = resolved with { DefaultModel = model };
        }

        return new ProviderResolutionResult.Resolved(resolved);
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

    private static Result<Provider> FindByName(LauncherSettings settings, string name) =>
        settings.Providers.FirstOrDefault(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match
            ? Result.Ok(match)
            : Result.Fail<Provider>("");

    private static string UnknownNameMessage(LauncherSettings settings, string name)
    {
        string message = $"unknown backend name '{name}'.";

        // Best-effort close-match hint; non-contractual UX that may be
        // improved or dropped without changing the resolution outcome.
        Result<string> hint = CloseMatch.Best(settings.Providers.Select(b => b.Name), name);
        if (hint.Success)
        {
            message += $" Did you mean '{hint.Value}'?";
        }

        return message;
    }

    private static ProviderResolutionResult Fail(ProviderResolutionFailure failure, string message) =>
        new ProviderResolutionResult.Failed(failure, message);
}