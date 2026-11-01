using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Backends;

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

    /// <summary>
    /// At rest this is a secure-store reference (<c>hgkey:&lt;name&gt;</c>) or,
    /// for keys predating the migration, the legacy plaintext value.
    /// In memory, it is a secret only after <c>BackendKeyResolver</c> has resolved
    /// it for a launch or for the wizard; the settings object itself is never
    /// mutated with secrets, so every save path writes a reference back.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }
}