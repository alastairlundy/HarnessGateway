using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// One entry of the pre-provider <c>backends</c> array - part of the legacy
/// schemaVersion 1-2 read model that <see cref="LegacySettings"/> converts on
/// load. Deleted together with that type and the legacy reader in the first
/// release after v1.
/// </summary>
[Obsolete("Legacy schemaVersion 1-2 read model. Delete this type, LegacySettings, and the legacy reader in the first release after v1.")]
public sealed record LegacyBackend
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("baseUri")]
    public required Uri BaseUri { get; init; }

    [JsonPropertyName("protocol")]
    public required ProtocolKind Protocol { get; init; }

    [JsonPropertyName("defaultModel")]
    public string? DefaultModel { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }
}
