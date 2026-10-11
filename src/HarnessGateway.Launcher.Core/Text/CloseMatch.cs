namespace HarnessGateway.Launcher.Core.Text;

/// <summary>
/// Best-effort "did you mean" matching for user-typed names. Shared by the
/// backend-name and agent-name resolvers so both CLI surfaces produce the same
/// shape of suggestion.
/// </summary>
/// <remarks>
/// Non-contractual UX. A resolver may improve or drop the hint without changing
/// its success or failure outcome, so tests must not pin the hint text.
/// </remarks>
public static class CloseMatch
{
    private const int MaxDistance = 2;

    /// <summary>
    /// The closest candidate to <paramref name="name"/> on success, or a failed
    /// outcome when nothing is close. Prefers a shared prefix, then falls back
    /// to edit distance. The failure carries the empty string: the hint is
    /// non-contractual UX with no message of its own.
    /// </summary>
    public static Result<string> Best(IEnumerable<string> candidates, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Result.Fail<string>("");
        }

        List<string> ordered = [.. candidates];

        foreach (string candidate in ordered)
        {
            if (candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Ok(candidate);
            }
        }

        foreach (string candidate in ordered)
        {
            if (WithinEditDistance(candidate, name, MaxDistance))
            {
                return Result.Ok(candidate);
            }
        }

        return Result.Fail<string>("");
    }

    /// <summary>
    /// True when the two strings are within <paramref name="maxDistance"/>
    /// edits of each other. The cap lets each row bail out as soon as every cell
    /// exceeds the budget, which keeps the common near-miss case cheap.
    /// </summary>
    private static bool WithinEditDistance(string a, string b, int maxDistance)
    {
        if (Math.Abs(a.Length - b.Length) > maxDistance)
        {
            return false;
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > maxDistance)
            {
                return false;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= maxDistance;
    }
}
