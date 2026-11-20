namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// Shared endpoint-URL text handling for the paths that hand a base URL to a
/// harness or echo it back to the user (the emission planner and the
/// wizard's endpoint steps). Pure and stateless; no rewriting beyond the
/// trailing-path-slash trim - the launcher never normalises an endpoint.
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
}
