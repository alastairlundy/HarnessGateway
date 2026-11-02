using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed record LauncherSettings
{
    /// <summary>
    /// The schema version this build reads and writes. Version 1 is the legacy
    /// plaintext-key shape and version 2 the legacy <c>backends</c> shape;
    /// <see cref="SettingsStore.LoadAsync"/> migrates both forward on read
    /// through the legacy read model, so a fresh object and every file this
    /// tool writes carry the current version.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    [JsonPropertyName("providers")]
    public List<Provider> Providers { get; init; } = [];

    [JsonPropertyName("defaultProvider")]
    public string DefaultProvider { get; init; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
}
