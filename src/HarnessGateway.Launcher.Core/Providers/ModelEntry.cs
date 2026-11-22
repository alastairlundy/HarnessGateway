using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// One model entry inside a provider's model catalog: a required name plus
/// optional protocol and endpoint overrides. A null <see cref="Protocol"/> or
/// <see cref="Endpoint"/> means the provider default applies; an override wins
/// at launch for that model only. Entry names are unique within a provider,
/// and the credential stays provider-level - an entry never carries a key.
/// </summary>
public sealed record ModelEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Null means the provider's default protocol applies to this model.</summary>
    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProtocolKind? Protocol { get; init; }

    /// <summary>Null means the provider's default endpoint applies to this model.</summary>
    [JsonPropertyName("endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Endpoint { get; init; }
}
