using System.Text.Json.Serialization;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed record LauncherSettings
{
    /// <summary>
    /// The schema version this build reads and writes. Version 1 is the legacy
    /// shape (plaintext keys); <see cref="SettingsStore.Load"/> migrates it
    /// forward on read, so a fresh object and every file this tool writes carry
    /// the current version.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("backends")]
    public List<Backend> Backends { get; init; } = [];

    [JsonPropertyName("defaultBackend")]
    public string DefaultBackend { get; init; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
}
