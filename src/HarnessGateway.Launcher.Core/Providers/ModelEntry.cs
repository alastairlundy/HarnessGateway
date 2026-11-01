using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// One model entry inside a provider's model catalog: a required name plus an
/// optional protocol override. A null <see cref="Protocol"/> means the provider
/// default applies; an override wins at launch for that model only. Entry names
/// are unique within a provider.
/// </summary>
public sealed record ModelEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Null means the provider's default protocol applies to this model.</summary>
    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProtocolKind? Protocol { get; init; }
}
