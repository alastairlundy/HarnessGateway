namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// The one shared protocol-to-env-var mapping, consolidated from the
/// per-adapter spellings every adapter emits today. Pure and stateless: no
/// settings or provider context in, one <see cref="ProtocolEmission"/> out.
/// Per-harness config shapes (OpenCode's inline session config,
/// Copilot's provider block, Claude Code's auth-token rename) stay
/// adapter-owned and are never emitted here.
/// </summary>
public sealed class ProtocolEmissionPlanner : IProtocolEmissionPlanner
{
    public ProtocolEmission Plan(ProtocolKind protocol, Uri baseUri)
    {
        string baseUrl = EndpointText.TrimTrailingPathSlash(baseUri);

        return protocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => new ProtocolEmission
            {
                Protocol = protocol,
                BaseUrlVariable = "OPENAI_BASE_URL",
                BaseUrlValue = baseUrl,
                ExtraVariables = ApiKeySlot("OPENAI_API_KEY"),
            },

            ProtocolKind.Anthropic => new ProtocolEmission
            {
                Protocol = protocol,
                BaseUrlVariable = "ANTHROPIC_BASE_URL",
                BaseUrlValue = baseUrl,
                ExtraVariables = ApiKeySlot("ANTHROPIC_API_KEY"),
            },

            _ => throw new ArgumentOutOfRangeException(
                nameof(protocol), protocol, "unsupported protocol kind"),
        };
    }

    /// <summary>
    /// The API-key variable of a protocol as an empty-valued slot: the
    /// planner is given no key, so it contributes the name and the
    /// adapter fills the value from the resolved secret - or omits the entry
    /// entirely when there is no secret, so keyless launches never emit an
    /// empty environment variable.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ApiKeySlot(string variableName) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [variableName] = string.Empty };
}
