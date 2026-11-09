namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// Typed failure classes for config-key addressing. Callers map these to
/// their own exit codes; the resolver itself never exits.
/// </summary>
public enum ConfigKeyFailure
{
    None = 0,

    /// <summary>The key is not in the closed typed key set.</summary>
    UnknownKey,

    /// <summary>The key names a read-only setting (schemaVersion, backends).</summary>
    ReadOnlyKey,

    /// <summary>The key shape is valid but names no saved provider entry.</summary>
    UnaddressableKey,

    /// <summary>The value cannot be applied to the addressed setting.</summary>
    InvalidValue,

    /// <summary>A defaultProvider write named no saved provider entry.</summary>
    UnknownProviderName,
}