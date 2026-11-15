namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// The closed typed key set over <see cref="LauncherSettings"/>: exactly
/// <c>defaultProvider</c>, read-only <c>schemaVersion</c> and <c>providers</c>
/// (names only), and the per-entry fields
/// <c>providers.&lt;name&gt;.endpoint</c>,
/// <c>providers.&lt;name&gt;.protocol</c>,
/// <c>providers.&lt;name&gt;.defaultModel</c>. Nothing here addresses secret
/// material, provider creation/deletion, or raw JSON paths - unknown or
/// unaddressable keys are usage errors. Model entries are deliberately not
/// scriptable: the catalog belongs to the <c>provider model</c> verbs.
/// </summary>
public static class ConfigKeyResolver
{
    private static readonly string[] WritableProviderFields =
        ["endpoint", "protocol", "defaultModel"];

    /// <summary>Reads a typed key. Fails with <see cref="ConfigKeyFailure.UnknownKey"/> for anything outside the set.</summary>
    public static ConfigKeyResult Get(LauncherSettings settings, string key)
    {
        if (string.Equals(key, "defaultProvider", StringComparison.Ordinal))
        {
            return Ok(settings.DefaultProvider);
        }

        if (string.Equals(key, "schemaVersion", StringComparison.Ordinal))
        {
            return Ok(settings.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (string.Equals(key, "providers", StringComparison.Ordinal))
        {
            // Names only - never endpoint, protocol, or key values.
            return Ok(string.Join(Environment.NewLine, settings.Providers.Select(b => b.Name)));
        }

        if (TryParseProviderFieldKey(key, out string name, out string field))
        {
            if (!WritableProviderFields.Contains(field, StringComparer.Ordinal))
            {
                return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
            }

            Result<Provider> found = FindProvider(settings, name);
            if (!found.Success)
            {
                return Fail(ConfigKeyFailure.UnaddressableKey, UnaddressableMessage(key, name));
            }

            Provider entry = found.Value;
            return field switch
            {
                "endpoint" => Ok(entry.BaseUri.ToString()),
                "protocol" => Ok(ProviderResolution.ProtocolName(entry.DefaultProtocol)),
                _ => Ok(entry.DefaultModel ?? string.Empty),
            };
        }

        return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
    }

    /// <summary>
    /// Writes a typed key. Mutates nothing on failure; on success either
    /// returns the updated settings snapshot (persist it) or marks the write
    /// <see cref="ConfigKeyResult.AlreadySatisfied"/> when no change was needed.
    /// </summary>
    public static ConfigKeyResult Set(LauncherSettings settings, string key, string value)
    {
        if (string.Equals(key, "defaultProvider", StringComparison.Ordinal))
        {
            return SetDefaultProvider(settings, value);
        }

        if (string.Equals(key, "schemaVersion", StringComparison.Ordinal))
        {
            return Fail(
                ConfigKeyFailure.ReadOnlyKey,
                "'schemaVersion' is read-only; it is stamped by the settings loader.");
        }

        if (string.Equals(key, "providers", StringComparison.Ordinal))
        {
            return Fail(
                ConfigKeyFailure.ReadOnlyKey,
                "'providers' is read-only; provider creation and deletion are not key-addressable - " +
                "use 'harnessgateway provider add' and 'harnessgateway provider remove'.");
        }

        if (TryParseProviderFieldKey(key, out string name, out string field))
        {
            if (!WritableProviderFields.Contains(field, StringComparer.Ordinal))
            {
                return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
            }

            Result<Provider> found = FindProvider(settings, name);
            if (!found.Success)
            {
                return Fail(ConfigKeyFailure.UnaddressableKey, UnaddressableMessage(key, name));
            }

            return SetProviderField(settings, found.Value, key, field, value);
        }

        return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
    }

    private static ConfigKeyResult SetProviderField(
        LauncherSettings settings, Provider entry, string key, string field, string value)
    {
        switch (field)
        {
            case "endpoint":
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
                {
                    return Fail(
                        ConfigKeyFailure.InvalidValue,
                        $"Invalid endpoint '{value}': must be an absolute URL.");
                }

                return ApplyField(settings, entry, key, entry with { BaseUri = uri });

            case "protocol":
                ProtocolKind? kind = ProviderResolution.TryParseProtocol(value);
                if (kind is null)
                {
                    return Fail(
                        ConfigKeyFailure.InvalidValue,
                        $"Invalid protocol '{value}'. Must be one of: openai-chat, openai-responses, anthropic");
                }

                return ApplyField(settings, entry, key, entry with { DefaultProtocol = kind.Value });

            default:
                return ApplyField(settings, entry, key, entry with { DefaultModel = value });
        }
    }

    /// <summary>
    /// The sole write path for the default pointer: validates the name against
    /// the saved entries (exit-8 class on failure, via the caller's mapping of
    /// <see cref="ConfigKeyFailure.UnknownProviderName"/>), succeeds idempotently
    /// when already default, and stores the canonical entry name.
    /// </summary>
    private static ConfigKeyResult SetDefaultProvider(LauncherSettings settings, string value)
    {
        ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, value);

        Provider entry;
        switch (resolution)
        {
            case ProviderResolutionResult.Resolved { Launch: LaunchResolution.ProviderCase providerCase }:
                entry = providerCase.Provider;
                break;

            case ProviderResolutionResult.Failed failed:
                // With no overrides supplied this failure is always the unknown-name class.
                return Fail(ConfigKeyFailure.UnknownProviderName, failed.Message);

            default:
                // EmptySettings is unreachable: the written value is a required
                // name, so resolution either finds the entry or fails.
                return Fail(ConfigKeyFailure.UnknownProviderName, $"unknown provider name '{value}'.");
        }

        if (string.Equals(settings.DefaultProvider, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new ConfigKeyResult
            {
                AlreadySatisfied = true,
                Settings = settings,
                Message = $"Default provider set to '{entry.Name}'.",
            };
        }

        return new ConfigKeyResult
        {
            Settings = settings with { DefaultProvider = entry.Name },
            Message = $"Default provider set to '{entry.Name}'.",
        };
    }

    private static ConfigKeyResult ApplyField(LauncherSettings settings, Provider entry, string key, Provider updated)
    {
        // Field writes never create entries and never touch ApiKey: only the
        // addressed endpoint/protocol/defaultModel changes.
        int index = settings.Providers.FindIndex(b =>
            string.Equals(b.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            settings.Providers[index] = updated;
        }
        else
        {
            settings.Providers.Add(updated);
        }

        return new ConfigKeyResult
        {
            Settings = settings,
            Message = $"{key} set to {DescribeField(key, updated)}",
        };
    }

    private static string DescribeField(string key, Provider updated) =>
        key.EndsWith(".endpoint", StringComparison.Ordinal)
            ? updated.BaseUri.ToString()
            : key.EndsWith(".protocol", StringComparison.Ordinal)
                ? ProviderResolution.ProtocolName(updated.DefaultProtocol)
                : updated.DefaultModel ?? string.Empty;

    /// <summary>
    /// Splits <c>providers.&lt;name&gt;.&lt;field&gt;</c> into its two trailing
    /// segments. False for anything else, including keys with extra dots or a
    /// missing field segment.
    /// </summary>
    private static bool TryParseProviderFieldKey(string key, out string name, out string field)
    {
        name = string.Empty;
        field = string.Empty;

        const string prefix = "providers.";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = key[prefix.Length..];
        int separator = rest.IndexOf('.');
        if (separator <= 0 || separator == rest.Length - 1)
        {
            return false;
        }

        name = rest[..separator];
        field = rest[(separator + 1)..];
        return !field.Contains('.', StringComparison.OrdinalIgnoreCase);
    }

    private static Result<Provider> FindProvider(LauncherSettings settings, string name) =>
        settings.Providers.FirstOrDefault(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match
            ? Result.Ok(match)
            : Result.Fail<Provider>("");

    private static ConfigKeyResult Ok(string value) =>
        new() { Value = value, Message = value };

    private static ConfigKeyResult Fail(ConfigKeyFailure failure, string message) =>
        new() { Failure = failure, Message = message };

    private static string UnknownKeyMessage(string key) =>
        $"Unknown settings key '{key}'. Valid keys: defaultProvider, schemaVersion, providers, " +
        "providers.<name>.endpoint, providers.<name>.protocol, providers.<name>.defaultModel.";

    private static string UnaddressableMessage(string key, string name) =>
        $"Unknown settings key '{key}': no provider entry named '{name}'. " +
        "Provider creation is not key-addressable; use 'harnessgateway provider add'.";
}
