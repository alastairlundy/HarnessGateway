namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// The closed typed key set over <see cref="LauncherSettings"/>: exactly
/// <c>defaultBackend</c>, read-only <c>schemaVersion</c> and <c>backends</c>
/// (names only), and the per-entry fields
/// <c>backends.&lt;name&gt;.endpoint</c>,
/// <c>backends.&lt;name&gt;.protocol</c>,
/// <c>backends.&lt;name&gt;.defaultModel</c>. Nothing here addresses secret
/// material, backend creation/deletion, or raw JSON paths - unknown or
/// unaddressable keys are usage errors.
/// </summary>
public static class ConfigKeyResolver
{
    private static readonly string[] WritableBackendFields =
        ["endpoint", "protocol", "defaultModel"];

    /// <summary>Reads a typed key. Fails with <see cref="ConfigKeyFailure.UnknownKey"/> for anything outside the set.</summary>
    public static ConfigKeyResult Get(LauncherSettings settings, string key)
    {
        if (string.Equals(key, "defaultBackend", StringComparison.Ordinal))
        {
            return Ok(settings.DefaultBackend);
        }

        if (string.Equals(key, "schemaVersion", StringComparison.Ordinal))
        {
            return Ok(settings.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (string.Equals(key, "backends", StringComparison.Ordinal))
        {
            // Names only - never endpoint, protocol, or key values.
            return Ok(string.Join(Environment.NewLine, settings.Backends.Select(b => b.Name)));
        }

        if (TryParseBackendFieldKey(key, out string name, out string field))
        {
            if (!WritableBackendFields.Contains(field, StringComparer.Ordinal))
            {
                return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
            }

            Backend? entry = FindBackend(settings, name);
            if (entry is null)
            {
                return Fail(ConfigKeyFailure.UnaddressableKey, UnaddressableMessage(key, name));
            }

            return field switch
            {
                "endpoint" => Ok(entry.BaseUri.ToString()),
                "protocol" => Ok(BackendResolution.ProtocolName(entry.Protocol)),
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
        if (string.Equals(key, "defaultBackend", StringComparison.Ordinal))
        {
            return SetDefaultBackend(settings, value);
        }

        if (string.Equals(key, "schemaVersion", StringComparison.Ordinal))
        {
            return Fail(
                ConfigKeyFailure.ReadOnlyKey,
                "'schemaVersion' is read-only; it is stamped by the settings loader.");
        }

        if (string.Equals(key, "backends", StringComparison.Ordinal))
        {
            return Fail(
                ConfigKeyFailure.ReadOnlyKey,
                "'backends' is read-only; backend creation and deletion are not key-addressable - " +
                "use 'harnessgateway config backend add' and 'harnessgateway config backend remove'.");
        }

        if (TryParseBackendFieldKey(key, out string name, out string field))
        {
            if (!WritableBackendFields.Contains(field, StringComparer.Ordinal))
            {
                return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
            }

            Backend? entry = FindBackend(settings, name);
            if (entry is null)
            {
                return Fail(ConfigKeyFailure.UnaddressableKey, UnaddressableMessage(key, name));
            }

            return SetBackendField(settings, entry, key, field, value);
        }

        return Fail(ConfigKeyFailure.UnknownKey, UnknownKeyMessage(key));
    }

    private static ConfigKeyResult SetBackendField(
        LauncherSettings settings, Backend entry, string key, string field, string value)
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
                ProtocolKind? kind = BackendResolution.TryParseProtocol(value);
                if (kind is null)
                {
                    return Fail(
                        ConfigKeyFailure.InvalidValue,
                        $"Invalid protocol '{value}'. Must be one of: openai-chat, openai-responses, anthropic");
                }

                return ApplyField(settings, entry, key, entry with { Protocol = kind.Value });

            default:
                return ApplyField(settings, entry, key, entry with { DefaultModel = value });
        }
    }

    /// <summary>
    /// The sole write path for the default pointer: validates the name against
    /// the saved entries (exit-8 class on failure, via the caller's mapping of
    /// <see cref="ConfigKeyFailure.UnknownBackendName"/>), succeeds idempotently
    /// when already default, and stores the canonical entry name.
    /// </summary>
    private static ConfigKeyResult SetDefaultBackend(LauncherSettings settings, string value)
    {
        BackendResolutionResult resolution = BackendResolution.Resolve(settings, value, endpoint: null, protocol: null, model: null);

        Backend entry;
        switch (resolution)
        {
            case BackendResolutionResult.Resolved resolved:
                entry = resolved.Backend;
                break;

            case BackendResolutionResult.Failed failed:
                // With no overrides supplied this failure is always the unknown-name class.
                return Fail(ConfigKeyFailure.UnknownBackendName, failed.Message);

            default:
                // EmptySettings is unreachable: the written value is a required
                // name, so resolution either finds the entry or fails.
                return Fail(ConfigKeyFailure.UnknownBackendName, $"unknown backend name '{value}'.");
        }

        if (string.Equals(settings.DefaultBackend, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new ConfigKeyResult
            {
                AlreadySatisfied = true,
                Settings = settings,
                Message = $"Default backend set to '{entry.Name}'.",
            };
        }

        return new ConfigKeyResult
        {
            Settings = settings with { DefaultBackend = entry.Name },
            Message = $"Default backend set to '{entry.Name}'.",
        };
    }

    private static ConfigKeyResult ApplyField(LauncherSettings settings, Backend entry, string key, Backend updated)
    {
        // Field writes never create entries and never touch ApiKey: only the
        // addressed endpoint/protocol/defaultModel changes.
        int index = settings.Backends.FindIndex(b =>
            string.Equals(b.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            settings.Backends[index] = updated;
        }
        else
        {
            settings.Backends.Add(updated);
        }

        return new ConfigKeyResult
        {
            Settings = settings,
            Message = $"{key} set to {DescribeField(key, updated)}",
        };
    }

    private static string DescribeField(string key, Backend updated) =>
        key.EndsWith(".endpoint", StringComparison.Ordinal)
            ? updated.BaseUri.ToString()
            : key.EndsWith(".protocol", StringComparison.Ordinal)
                ? BackendResolution.ProtocolName(updated.Protocol)
                : updated.DefaultModel ?? string.Empty;

    /// <summary>
    /// Splits <c>backends.&lt;name&gt;.&lt;field&gt;</c> into its two trailing
    /// segments. False for anything else, including keys with extra dots or a
    /// missing field segment.
    /// </summary>
    private static bool TryParseBackendFieldKey(string key, out string name, out string field)
    {
        name = string.Empty;
        field = string.Empty;

        const string prefix = "backends.";
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

    private static Backend? FindBackend(LauncherSettings settings, string name) =>
        settings.Backends.FirstOrDefault(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ConfigKeyResult Ok(string value) =>
        new() { Value = value, Message = value };

    private static ConfigKeyResult Fail(ConfigKeyFailure failure, string message) =>
        new() { Failure = failure, Message = message };

    private static string UnknownKeyMessage(string key) =>
        $"Unknown settings key '{key}'. Valid keys: defaultBackend, schemaVersion, backends, " +
        "backends.<name>.endpoint, backends.<name>.protocol, backends.<name>.defaultModel.";

    private static string UnaddressableMessage(string key, string name) =>
        $"Unknown settings key '{key}': no backend entry named '{name}'. " +
        "Backend creation is not key-addressable; use 'harnessgateway config backend add'.";
}
