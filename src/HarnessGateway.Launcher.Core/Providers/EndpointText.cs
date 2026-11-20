namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Shared endpoint-URL text handling for the paths that hand a base URL to a
/// harness or echo it back to the user (the emission planner and the
/// wizard's endpoint steps). Pure and stateless. <see cref="TrimTrailingPathSlash"/>
/// is the unconditional normalisation; <see cref="TrimTrailingResponsesSegment"/>
/// is an opt-in tool for the adapters whose harness appends the segment
/// itself - the planner never applies it.
/// </summary>
internal static class EndpointText
{
    /// <summary>
    /// Drops a trailing slash from the base URI's PATH only, leaving any
    /// query or fragment intact: trimming the whole string would corrupt a
    /// query that ends in <c>/</c> (<c>https://host/v1?next=/</c> became
    /// <c>https://host/v1?next=</c>). Common cases keep their contract:
    /// <c>https://host/v1/</c> becomes <c>https://host/v1</c>, and a bare
    /// origin <c>https://host/</c> becomes <c>https://host</c>.
    /// </summary>
    public static string TrimTrailingPathSlash(Uri baseUri)
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
    /// Like <see cref="TrimTrailingPathSlash"/> but also drops a single
    /// trailing <c>/responses</c> segment, so a base URL that already names
    /// the Responses endpoint is not doubled by harnesses that append it
    /// themselves. Query and fragment ride along unchanged.
    /// </summary>
    public static string TrimTrailingResponsesSuffix(Uri baseUri)
    {
        string text = TrimTrailingPathSlash(baseUri);

        int suffixStart = text.IndexOfAny(['?', '#']);
        string pathPart = suffixStart < 0 ? text : text[..suffixStart];
        string tail = suffixStart < 0 ? string.Empty : text[suffixStart..];

        const string segment = "/responses";
        if (pathPart.EndsWith(segment, StringComparison.OrdinalIgnoreCase))
        {
            pathPart = pathPart[..^segment.Length];
        }

        return pathPart + tail;
    }

    /// <summary>
    /// Path-only variant of <see cref="TrimTrailingResponsesSuffix"/>: trims a
    /// trailing slash and a single trailing <c>/responses</c> segment from a
    /// path string. Used where the path is handled separately from the rest of
    /// the URI (e.g. model-catalogue requests).
    /// </summary>
    public static string TrimTrailingResponsesSegment(string path)
    {
        string trimmed = path.TrimEnd('/');

        const string segment = "/responses";
        if (trimmed.EndsWith(segment, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^segment.Length];
        }

        return trimmed;
    }
}
