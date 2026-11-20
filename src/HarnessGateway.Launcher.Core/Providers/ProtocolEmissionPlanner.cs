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
        string baseUrl = TrimTrailingPathSlash(baseUri);

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
    /// Drops a trailing slash from the base URI's PATH only, leaving any
    /// query or fragment intact: trimming the whole string would corrupt a
    /// query that ends in <c>/</c> (<c>https://host/v1?next=/</c> became
    /// <c>https://host/v1?next=</c>). Common cases keep their contract:
    /// <c>https://host/v1/</c> becomes <c>https://host/v1</c>, and a bare
    /// origin <c>https://host/</c> becomes <c>https://host</c>.
    /// </summary>
    private static string TrimTrailingPathSlash(Uri baseUri)
    {
        string text = baseUri.ToString();

        // The first '?' or '#' starts the query/fragment suffix; everything
        // before it is scheme + authority + path and is the only part a
        // trailing slash may be trimmed from.
        int suffixStart = text.IndexOfAny(['?', '#']);
        if (suffixStart < 0)
        {
            return text.TrimEnd('/');
        }

        return text[..suffixStart].TrimEnd('/') + text[suffixStart..];
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
