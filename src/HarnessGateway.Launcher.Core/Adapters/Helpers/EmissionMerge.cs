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
