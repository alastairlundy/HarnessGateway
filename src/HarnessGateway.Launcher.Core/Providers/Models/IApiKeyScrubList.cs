namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// The curated set of credential-bearing environment variables that the
/// Environment Variable Scrub removes from the spawned harness process when
/// the launch asks for it. Membership is a closed, pre-determined list - no
/// pattern heuristics - and matching follows the platform's casing rule.
/// </summary>
public interface IApiKeyScrubList
{
    /// <summary>The names as written in the curated list.</summary>
    IReadOnlyCollection<string> VariableNames { get; }

    /// <summary>
    /// Whether <paramref name="variableName"/> names a listed variable under
    /// this platform's casing rule: case-insensitive on Windows, case-sensitive
    /// elsewhere, where environment variable names are case-sensitive.
    /// </summary>
    bool Contains(string variableName);
}
