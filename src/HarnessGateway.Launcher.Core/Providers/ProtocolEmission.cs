namespace HarnessGateway.Launcher.Core.Providers;

/// <summary>
/// The env-var emission for one protocol: the base-url variable plus any
/// extra variables that protocol carries. Adapters map these into
/// <see cref="Launch.LaunchPlan"/>'s process environment only (T016, T007);
/// harness-specific emission additions stay adapter-side, never here.
/// </summary>
public sealed record ProtocolEmission
{
    /// <summary>The protocol this emission was planned for.</summary>
    public required ProtocolKind Protocol { get; init; }

    /// <summary>Name of the environment variable carrying the base URL.</summary>
    public required string BaseUrlVariable { get; init; }

    /// <summary>The base URL value, with any trailing slash trimmed.</summary>
    public required string BaseUrlValue { get; init; }

    /// <summary>
    /// The protocol's additional variables. The API-key variable name rides
    /// here as a key with an empty-string value (T017): the planner receives
    /// no key, so it carries the naming only and the adapter that holds the
    /// secret fills the value. Variables of unrelated protocols never appear
    /// in this dictionary (T007).
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraVariables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
