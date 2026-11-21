using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Text;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Profile-aware launch resolution shared by the launch path and the
/// config/wizard read paths: named-entry selection, the strict
/// <c>DefaultProvider</c> pointer read (no first-entry fallback, no
/// auto-promotion), precedence and override layering onto the sealed
/// two-case <see cref="LaunchResolution"/> family, and the launch-time
/// unlisted-model guard. Selection is always explicit - nothing here
/// probes endpoints.
/// </summary>
public static class ProviderResolution
{
    private static readonly string[] ProtocolNames =
        ["openai-chat", "openai-responses", "anthropic"];

    /// <summary>
    /// Resolve a launch: select a saved entry (by name when
    /// <paramref name="providerName"/> is given, otherwise via the strict
    /// default-pointer read), layer the flag pair and per-model precedence on
    /// top of it, and return the provider case or - when no saved provider
    /// exists and the flag pair is complete - the keyless ad-hoc case. The
    /// unlisted-model guard applies here: a named effective model outside a
    /// non-empty models array is refused (exit 3 upstream).
    /// </summary>
    public static ProviderResolutionResult Resolve(
        LauncherSettings settings,
        string? providerName,
        string? endpoint,
        string? protocol,
        string? model)
        => Resolve(settings, providerName, endpoint, protocol, model, applyUnlistedModelGuard: true);



    /// <summary>
    /// Resolve a saved entry for the management and read paths (config
    /// pointer writes, key lifecycle, the wizard's prefill read): identical
    /// selection, pairing, and failure shapes, but no unlisted-model guard -
    /// that refusal belongs to launch and must never block writing a pointer
    /// or removing, re-keying, or repairing the provider that carries a
    /// broken default model.
    /// </summary>
    public static ProviderResolutionResult ResolveEntry(
        LauncherSettings settings,
        string? providerName)
        => Resolve(settings, providerName, endpoint: null, protocol: null, model: null,
            applyUnlistedModelGuard: false);

    public static ProviderResolutionResult Resolve(
        LauncherSettings settings,
        string? providerName,
        string? endpoint,
        string? protocol,
        string? model,
        bool applyUnlistedModelGuard)
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

        // Select the entry: --provider resolves against the saved list; without
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
                    DanglingPointerMessage(settings));
            }

            entry = defaultEntry.Value;
        }
        else if (settings.Providers.Count > 0)
        {
            return Fail(
                ProviderResolutionFailure.AbsentDefaultProvider,
                "no default provider is set while saved provider entries exist. " +
                "Run 'harnessgateway config set defaultProvider <name>' to choose a default provider.");
        }
        else
        {
            entry = null;
        }

        if (entry is null)
        {
            // Nothing stored to layer onto: a complete raw URL still launches
            // as the keyless ad-hoc case; otherwise there is no provider at all.
            if (parsedEndpoint is not null && parsedProtocol is not null)
            {
                return new ProviderResolutionResult.Resolved(
                    new LaunchResolution.AdHocCase(parsedEndpoint, parsedProtocol.Value, model));
            }

            return new ProviderResolutionResult.EmptySettings();
        }

        // Precedence: the effective model is --model else the stored default;
        // absent a flag pair the effective model's listed entry layers both of
        // its overrides - endpoint and protocol - over the provider defaults,
        // through the same single lookup. The flag pair wins wholesale for the
        // session and exempts the launch from the unlisted-model guard, so a
        // stored per-model endpoint is silently ignored when it is passed.
        string? effectiveModel = model ?? entry.DefaultModel;
        Uri sessionBaseUri;
        ProtocolKind sessionProtocol;

        if (parsedEndpoint is not null && parsedProtocol is not null)
        {
            sessionBaseUri = parsedEndpoint;
            sessionProtocol = parsedProtocol.Value;
        }
        else
        {
            sessionBaseUri = entry.BaseUri;
            sessionProtocol = entry.DefaultProtocol;

            if (applyUnlistedModelGuard && effectiveModel is not null && entry.Models.Count > 0)
            {
                ModelEntry? listed = entry.Models.FirstOrDefault(m =>
                    string.Equals(m.Name, effectiveModel, StringComparison.OrdinalIgnoreCase));

                if (listed is null)
                {
                    return Fail(
                        ProviderResolutionFailure.UnlistedModel,
                        UnlistedModelMessage(entry, effectiveModel));
                }

                sessionProtocol = listed.Protocol ?? entry.DefaultProtocol;
                sessionBaseUri = listed.Endpoint ?? entry.BaseUri;
            }
        }

        // The whole Provider travels with the case, layered for this session:
        // endpoint and default protocol carry the effective values, so
        // pre-planner consumers reading Provider.DefaultProtocol see the
        // protocol that will actually be emitted.
        Provider sessionProvider = entry with
        {
            BaseUri = sessionBaseUri,
            DefaultProtocol = sessionProtocol,
            DefaultModel = effectiveModel,
        };

        return new ProviderResolutionResult.Resolved(
            new LaunchResolution.ProviderCase(sessionProvider, effectiveModel, sessionProtocol));
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
        string message = $"unknown provider name '{name}'.";

        // Best-effort close-match hint; non-contractual UX that may be
        // improved or dropped without changing the resolution outcome.
        Result<string> hint = CloseMatch.Best(settings.Providers.Select(b => b.Name), name);
        if (hint.Success)
        {
            message += $" Did you mean '{hint.Value}'?";
        }

        return message;
    }

    private static string DanglingPointerMessage(LauncherSettings settings)
    {
        string message =
            $"unknown provider name '{settings.DefaultProvider}' in the default provider pointer.";

        Result<string> hint = CloseMatch.Best(
            settings.Providers.Select(b => b.Name), settings.DefaultProvider);
        if (hint.Success)
        {
            message += $" Did you mean '{hint.Value}'?";
        }

        return message +
               " Run 'harnessgateway config set defaultProvider <name>' to choose a default provider.";
    }

    private static string UnlistedModelMessage(Provider provider, string model)
    {
        string message = $"model '{model}' is not listed for provider '{provider.Name}'.";

        // Best-effort close-match hint over the listed names; non-contractual
        // UX, same rule as the name-resolution hints above.
        Result<string> hint = CloseMatch.Best(provider.Models.Select(m => m.Name), model);
        if (hint.Success)
        {
            message += $" Did you mean '{hint.Value}'?";
        }

        return message +
               $" Pass a listed model with --model, or add it with 'harnessgateway provider model add " +
               $"{provider.Name} {model}'.";
    }

    private static ProviderResolutionResult Fail(ProviderResolutionFailure failure, string message) =>
        new ProviderResolutionResult.Failed(failure, message);
}
