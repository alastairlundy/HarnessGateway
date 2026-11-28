namespace HarnessGateway.Launcher.Core.Providers.Models;

/// <summary>
/// The one place the scrub list is enumerated. Seeded from the launcher's
/// protocol env-name table plus known provider keys; extending the list is a
/// release decision, never a runtime heuristic.
/// </summary>
public sealed class ApiKeyScrubList : IApiKeyScrubList
{
    // Windows environment names are case-insensitive, so a variable seeded as
    // `openai_api_key` still names OPENAI_API_KEY there. Unix names (including
    // macOS's) are case-sensitive.
    private static readonly StringComparer NameComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly IReadOnlyList<string> Names = Array.AsReadOnly<string>(
    [
        "OPENAI_API_KEY",      // ProtocolEmissionPlanner: openai-chat / openai-responses
        "ANTHROPIC_API_KEY",   // ProtocolEmissionPlanner: anthropic
        "ZAI_API_KEY",         // known provider key
    ]);

    public IReadOnlyCollection<string> VariableNames => Names;

    public bool Contains(string variableName) =>
        Names.Any(name => NameComparer.Equals(name, variableName));
}
