using System.Text.Json.Serialization;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// The pre-provider settings shape (schemaVersion 1 and 2: a <c>backends</c>
/// array plus the <c>defaultBackend</c> pointer), read only so
/// <see cref="SettingsStore.LoadAsync"/> can convert stale files forward on
/// load. This type and <see cref="LegacyBackend"/> exist only for that
/// migration and are deleted together with the legacy reader in the first
/// release after v1; they are never written back to disk.
/// </summary>
[Obsolete("Legacy schemaVersion 1-2 read model. Delete this type, LegacyBackend, and the legacy reader in the first release after v1.")]
public sealed record LegacySettings
{
    [JsonPropertyName("backends")]
    public IList<LegacyBackend> Backends { get; init; } = [];

    [JsonPropertyName("defaultBackend")]
    public string DefaultBackend { get; init; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Maps this legacy shape onto <see cref="LauncherSettings"/> per the
    /// migration rules: each backend becomes one Provider with its protocol as
    /// the default protocol, its defaultModel carried over, and an empty model
    /// list; <c>defaultBackend</c> maps to <c>defaultProvider</c> verbatim, so
    /// a dangling pointer stays dangling and fails later at the strict-pointer
    /// read; the result is stamped with the current schema version.
    /// </summary>
    public static LauncherSettings ToLauncherSettings(LegacySettings legacy) =>
        new()
        {
            Providers =
            [
                .. legacy.Backends.Select(static backend => new Provider
                {
                    Name = backend.Name,
                    BaseUri = backend.BaseUri,
                    DefaultProtocol = backend.Protocol,
                    DefaultModel = backend.DefaultModel,
                    ApiKey = backend.ApiKey,
                    Models = [],
                }),
            ],
            DefaultProvider = legacy.DefaultBackend,
            SchemaVersion = LauncherSettings.CurrentSchemaVersion,
        };
}
