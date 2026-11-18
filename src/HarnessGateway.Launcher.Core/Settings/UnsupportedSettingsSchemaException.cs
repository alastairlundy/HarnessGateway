namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// Failure raised when the settings file's declared <c>schemaVersion</c> cannot
/// be read by this build: the field is missing, is not a supported version
/// number, or names a version newer than the tool understands. Messages carry
/// the "unsupported settings schema version" phrase, the file path, the
/// declared vs. supported version, and a remedy - never secret material.
/// Callers map this to exit code 9 (<c>UnsupportedSettingsSchema</c>).
/// </summary>
public sealed class UnsupportedSettingsSchemaException : Exception
{
    private UnsupportedSettingsSchemaException(string message)
        : base(message)
    {
    }

    /// <summary>The settings file declares no schemaVersion field at all.</summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="legacyShape">
    /// True when the document carries the legacy <c>backends</c> shape. A
    /// legacy-shaped file may only be told to declare version 1 - the advice
    /// to declare the current version would read it as an empty current-shape
    /// file and lose its entries at the next save.
    /// </param>
    public static UnsupportedSettingsSchemaException Missing(string path, bool legacyShape = false) =>
        new(
            $"The settings file '{path}' declares no schemaVersion field: " +
            $"unsupported settings schema version (supported: 1 through {LauncherSettings.CurrentSchemaVersion}). " +
            (legacyShape
                ? "Add \"schemaVersion\": 1 - this file uses the legacy backends shape, and this build reads "
                  + "version 1 through the legacy reader and migrates it forward to the current shape on the "
                  + "next load - or delete the file to start fresh. The file was left untouched."
                : "Add \"schemaVersion\" - 1 for a file written by an older HarnessGateway, "
                  + $"{LauncherSettings.CurrentSchemaVersion} for a current one - or delete the file to start fresh. "
                  + "The file was left untouched."));

    /// <summary>The schemaVersion field is present but is not a supported version number.</summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="declaredValue">The offending raw field value.</param>
    /// <param name="legacyShape">
    /// True when the document carries the legacy <c>backends</c> shape; see
    /// <see cref="Missing"/> for why such a file is only ever told to declare 1.
    /// </param>
    public static UnsupportedSettingsSchemaException Malformed(
        string path,
        string declaredValue,
        bool legacyShape = false) =>
        new(
            $"The settings file '{path}' declares schemaVersion {declaredValue}: " +
            $"unsupported settings schema version (supported: 1 through {LauncherSettings.CurrentSchemaVersion}). " +
            (legacyShape
                ? "Set the field to 1 - this file uses the legacy backends shape, and this build migrates it "
                  + "forward to the current shape on the next read - or delete the file to start fresh. "
                  + "The file was left untouched."
                : "Set the field to 1 for a file written by an older HarnessGateway or to "
                  + $"{LauncherSettings.CurrentSchemaVersion} for a current one, or delete the file to start fresh. "
                  + "The file was left untouched."));

    /// <summary>The file was written by a newer build than this one.</summary>
    public static UnsupportedSettingsSchemaException Newer(string path, int declared, int supported) =>
        new(
            $"The settings file '{path}' declares schemaVersion {declared} but this build " +
            $"supports up to {supported}: unsupported settings schema version. " +
            $"Upgrade HarnessGateway to a build that understands version {declared}; " +
            "this build will not read or rewrite the file. The file was left untouched.");
}