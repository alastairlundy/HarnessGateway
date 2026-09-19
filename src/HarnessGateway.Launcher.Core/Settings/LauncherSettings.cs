using System.Text.Json.Serialization;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed record LauncherSettings
{
    [JsonPropertyName("backends")]
    public List<Backend> Backends { get; init; } = [];

    [JsonPropertyName("defaultBackend")]
    public string DefaultBackend { get; init; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;
}
