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
/// for labels and prose, and treating it as an input token let
/// `launch copilot` fail while the help text advertised it.
/// </remarks>
public static class HarnessLookup
{
    /// <summary>
    /// The adapter whose command name or an alias equals <paramref name="token"/>,
    /// or a failed outcome when nothing matches. The failure carries the empty
    /// string: the caller decides the user-facing message.
    /// </summary>
    public static Result<IHarnessAdapter> TryResolve(
        IEnumerable<IHarnessAdapter> adapters,
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail<IHarnessAdapter>("");
        }

        return adapters.FirstOrDefault(adapter => Matches(adapter.Metadata, token)) is { } match
            ? Result.Ok(match)
            : Result.Fail<IHarnessAdapter>("");
    }

    /// <summary>
    /// The user-facing message for an unresolvable harness identifier,
    /// carrying a best-effort suggestion when a command name or alias is
    /// close. The hint is non-contractual UX: it offers command names only,
    /// never aliases, so it teaches the documented spelling rather than a
    /// hidden one.
    /// </summary>
    public static string UnknownAgentMessage(
        IEnumerable<IHarnessAdapter> adapters,
        string token)
    {
        string message = $"Unknown harness: {token}. Run 'harnessgateway launch' with no " +
                         "options for the interactive harness picker.";

        Result<string> hint = CloseMatch.Best(adapters.Select(a => a.Metadata.CommandName), token);
        if (hint.Success)
        {
            message += $" Did you mean '{hint.Value}'?";
        }

        return message;
    }

    private static bool Matches(HarnessMetadata metadata, string token) =>
        string.Equals(metadata.CommandName, token, StringComparison.OrdinalIgnoreCase)
        || metadata.Aliases.Any(alias =>
            string.Equals(alias, token, StringComparison.OrdinalIgnoreCase));
}
