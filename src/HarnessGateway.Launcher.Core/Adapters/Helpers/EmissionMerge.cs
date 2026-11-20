namespace HarnessGateway.Launcher.Core.Adapters.Helpers;

/// <summary>
/// The one consumption rule for the shared protocol emission: the
/// base-url variable always lands, each extra variable lands with its
/// planned value - or with the launch-scoped key filling its empty slot -
/// and a slot that stays empty is skipped, never emitted as an empty
/// variable. Harnesses that rename the base-url or key variable (Copilot's
/// provider block, Claude Code's auth-token rename, Codex's env_key) map
/// the emission's values themselves instead of calling this.
/// </summary>
public static class EmissionMerge
{
    /// <summary>
    /// The adapter-side opt-in for harnesses that append <c>/responses</c>
    /// to the base URL themselves: Codex joins the provider-relative path
    /// (<c>wire_api = "responses"</c>) onto <c>base_url</c>, and the
    /// OpenAI-family SDKs behind the opencode engine (OpenCode CLI and
    /// desktop, Kilo) and Pi post to <c>{baseURL}/responses</c>. An endpoint
    /// that already ends in the segment would be doubled, so the single
    /// trailing occurrence is dropped from the emission's base URL here -
    /// adapter-owned because it encodes those harnesses' URL behaviour, and
    /// never in the shared planner. Adapters whose harness has no Responses
    /// surface (Copilot's provider type collapses both OpenAI protocols to
    /// "openai") do not call this and keep the endpoint exactly as
    /// configured.
    /// </summary>
    public static ProtocolEmission DropResponsesSegment(ProtocolEmission emission)
    {
        if (emission.Protocol != ProtocolKind.OpenAiResponses)
        {
            return emission;
        }

        // The emission's value is already the planner's slash-trimmed
        // endpoint; the same Uri-based normalisation re-applied to it is
        // idempotent and only ever removes the segment.
        return emission with
        {
            BaseUrlValue = EndpointText.TrimTrailingResponsesSuffix(new Uri(emission.BaseUrlValue)),
        };
    }
    /// <summary>
    /// Merges <paramref name="emission"/> into
    /// <paramref name="environmentVariables"/>: the base-url variable plus
    /// every extra the planner filled, with <paramref name="apiKey"/>
    /// supplying the value of the protocol's empty key slot (skipped when
    /// the launch carries no key).
    /// </summary>
    public static void MergeInto(
        IDictionary<string, string> environmentVariables,
        ProtocolEmission emission,
        string? apiKey)
    {
        environmentVariables[emission.BaseUrlVariable] = emission.BaseUrlValue;

        foreach ((string name, string value) in emission.ExtraVariables)
        {
            string effective = value.Length > 0 ? value : apiKey ?? string.Empty;
            if (effective.Length > 0)
            {
                environmentVariables[name] = effective;
            }
        }
    }
}
