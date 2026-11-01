using System.Text.Json.Serialization;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// One provider identity: a single endpoint, a single at-rest key reference,
/// one default wire protocol, and an optional ordered model catalog where each
/// entry may override that default. At rest <see cref="ApiKey"/> is a secure-store
/// reference (<c>hgkey:&lt;name&gt;</c>) or, for keys predating the migration,
/// the legacy plaintext value; in memory it is a secret only after the
/// provider key resolver has resolved it for a launch or for the wizard, so
/// every save path writes a reference back.
/// </summary>
public sealed record Provider
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("baseUri")]
    public required Uri BaseUri { get; init; }

    [JsonPropertyName("defaultProtocol")]
    public required ProtocolKind DefaultProtocol { get; init; }

    [JsonPropertyName("defaultModel")]
    public string? DefaultModel { get; init; }

    /// <summary>
    /// Opaque credential pointer at rest (<c>hgkey:&lt;name&gt;</c>), never
    /// secret material; null until a key is stored.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// The model catalog in declaration order. An empty list keeps the open
    /// model path on <see cref="DefaultProtocol"/>; a non-empty list is the
    /// closed set launch resolution will accept.
    /// </summary>
    [JsonPropertyName("models")]
    public List<ModelEntry> Models { get; init; } = [];
}
