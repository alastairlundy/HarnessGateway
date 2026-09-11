using System.Text.Json.Serialization;

namespace LmProxy.AgentLauncher.Core.Backends;

public sealed record Backend
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
