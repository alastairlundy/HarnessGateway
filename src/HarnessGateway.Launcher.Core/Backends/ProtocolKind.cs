using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Backends;

public enum ProtocolKind
{
    [JsonPropertyName("openai-chat")]
    OpenAiChat,

    [JsonPropertyName("openai-responses")]
    OpenAiResponses,

    [JsonPropertyName("anthropic")]
    Anthropic
}
