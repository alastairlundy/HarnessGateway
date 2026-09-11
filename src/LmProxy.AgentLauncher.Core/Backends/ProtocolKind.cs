using System.Text.Json.Serialization;

namespace LmProxy.AgentLauncher.Core.Backends;

public enum ProtocolKind
{
    [JsonPropertyName("openai-chat")]
    OpenAiChat,

    [JsonPropertyName("openai-responses")]
    OpenAiResponses,

    [JsonPropertyName("anthropic")]
    Anthropic
}
