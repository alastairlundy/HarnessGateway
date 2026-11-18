using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// Source-generated serializer for model-catalog HTTP payloads. The DTO types are
/// internal so the context can see them; their wire shape is pinned by
/// <see cref="JsonPropertyNameAttribute"/> attributes.
/// </summary>
[JsonSerializable(typeof(ModelCatalogClient.OpenAiModelsResponse))]
[JsonSerializable(typeof(ModelCatalogClient.AnthropicModelsResponse))]
internal partial class ModelCatalogJsonContext : JsonSerializerContext;
