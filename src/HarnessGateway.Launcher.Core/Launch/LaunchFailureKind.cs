namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Typed failure classes for a launch that never produced an exit status.
/// Callers map these to their own exit codes; the flow itself never exits.
/// </summary>
public enum LaunchFailureKind
{
    /// <summary>The credential store's reference names no stored key.</summary>
    CredentialStoreAbsentKey,

    /// <summary>The credential store operation itself failed.</summary>
    CredentialStoreError,

    /// <summary>The settings file declares a schema version this build cannot read.</summary>
    UnsupportedSettingsSchema,

    /// <summary>An unexpected exception reached the terminal fold.</summary>
    Unexpected,
}
