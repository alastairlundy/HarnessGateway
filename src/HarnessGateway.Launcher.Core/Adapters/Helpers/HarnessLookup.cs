using HarnessGateway.Launcher.Core.Text;

namespace HarnessGateway.Launcher.Core.Adapters.Helpers;

/// <summary>
/// Resolves a user-typed agent token to its adapter.
/// </summary>
/// <remarks>
/// This is the single lookup used by the launch path and the wizard, so a token
/// that resolves in one resolves in the other. Matching is case-insensitive
/// equality against <see cref="HarnessMetadata.CommandName"/> and
/// <see cref="HarnessMetadata.Aliases"/>. The display
/// <see cref="HarnessMetadata.Name"/> is deliberately NOT matchable: it exists
/// for labels and prose, and treating it as an input token is what let
/// `launch copilot` fail while the help text advertised it.
/// </remarks>
public static class HarnessLookup
{
    /// <summary>
    /// The adapter whose command name or an alias equals <paramref name="token"/>,
    /// or null when nothing matches.
    /// </summary>
    public static IHarnessAdapter? Resolve(
        IEnumerable<IHarnessAdapter> adapters,
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return adapters.FirstOrDefault(adapter => Matches(adapter.Metadata, token));
    }

    /// <summary>
    /// The user-facing message for an unresolvable agent token, carrying a
    /// best-effort suggestion when a command name or alias is close.
    /// </summary>
    public static string UnknownAgentMessage(
        IEnumerable<IHarnessAdapter> adapters,
        string token)
    {
        string message = $"Unknown agent: {token}. Run 'harnessgateway launch' with no " +
                         "options for the interactive harness picker.";

        string? hint = Suggest(adapters, token);
        if (hint is not null)
        {
            message += $" Did you mean '{hint}'?";
        }

        return message;
    }

    /// <summary>
    /// The accepted token closest to <paramref name="token"/>. Offers command
    /// names only, never aliases, so the hint teaches the documented spelling
    /// rather than a hidden one.
    /// </summary>
    public static string? Suggest(IEnumerable<IHarnessAdapter> adapters, string token) =>
        CloseMatch.Best(adapters.Select(a => a.Metadata.CommandName), token);

    private static bool Matches(HarnessMetadata metadata, string token) =>
        string.Equals(metadata.CommandName, token, StringComparison.OrdinalIgnoreCase)
        || metadata.Aliases.Any(alias =>
            string.Equals(alias, token, StringComparison.OrdinalIgnoreCase));
}
