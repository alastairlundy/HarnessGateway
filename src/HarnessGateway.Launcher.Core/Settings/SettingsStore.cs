using System.Text.Json;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Serialization;
using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed class SettingsStore
{
    private const string SchemaVersionPropertyName = "schemaVersion";

    /// <summary>
    /// Suffix of the temp file the load-time migration writes through before
    /// atomically replacing the settings file, so a failed rewrite can never
    /// leave a half-written file behind.
    /// </summary>
    private const string MigrationTempSuffix = ".migrating";

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly BackupService _backupService;
    private ISecureKeyStore? _keyStore;

    /// <param name="settingsDirectory">Directory holding the settings file; defaults to the platform config directory.</param>
    /// <param name="keyStore">Credential store for the v1 plaintext-key sweep; defaults to the platform store.</param>
    /// <param name="backupService">
    /// Backup flow used by the load-time migration. Callers that already own
    /// a <see cref="BackupService"/> over the canonical
    /// <c>&lt;settings dir&gt;/backups</c> directory (the composition root)
    /// share theirs; otherwise the store creates one over that directory, so
    /// the migration's backup-before-write markers are the same ones
    /// <see cref="BackupService.SelfHealAsync"/> replays.
    /// </param>
    public SettingsStore(
        string? settingsDirectory = null,
        ISecureKeyStore? keyStore = null,
        BackupService? backupService = null)
    {
        string directory = settingsDirectory ?? GetPlatformConfigDirectory();
        _settingsDirectory = directory;
        _settingsFilePath = Path.Combine(directory, "HarnessGateway-launcher.json");
        _keyStore = keyStore;
        _backupService = backupService ?? new BackupService(Path.Combine(directory, "backups"));
    }

    public string SettingsFilePath => _settingsFilePath;

    public string SettingsDirectory => _settingsDirectory;

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Crash recovery runs before the file is read: replaying leftover
        // backup markers first means a legacy conversion below always
        // converts the bytes the user actually has after the replay (T003).
        await _backupService.SelfHealAsync(cancellationToken).ConfigureAwait(false);

        if (!File.Exists(_settingsFilePath))
        {
            return new();
        }

        // One parse: the version probe and the body deserialization read the
        // same document instead of hammering the file text twice.
        string json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        int declaredVersion = ReadDeclaredVersion(root);

        if (declaredVersion == LauncherSettings.CurrentSchemaVersion)
        {
            return ReadCurrentShape(root, declaredVersion);
        }

        if (declaredVersion is 1 or 2)
        {
            (LauncherSettings migrated, byte[]? markerBytes) =
                await ConvertLegacyShape(root, declaredVersion, cancellationToken).ConfigureAwait(false);
            await RewriteThroughBackupFlowAsync(migrated, markerBytes, cancellationToken).ConfigureAwait(false);
            return migrated;
        }

        if (declaredVersion > LauncherSettings.CurrentSchemaVersion)
        {
            throw UnsupportedSettingsSchemaException.Newer(
                _settingsFilePath,
                declaredVersion,
                LauncherSettings.CurrentSchemaVersion);
        }

        throw UnsupportedSettingsSchemaException.Malformed(
            _settingsFilePath,
            declaredVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HasLegacyShape(root));
    }

    /// <summary>
    /// Reads and validates the declared <c>schemaVersion</c>, throwing the
    /// documented unsupported-schema failures - with shape-aware remedies, so
    /// a legacy-shaped document is only ever told to declare a migratable
    /// version - for a missing field or a non-integer value.
    /// </summary>
    private int ReadDeclaredVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(SchemaVersionPropertyName, out JsonElement versionElement))
        {
            throw UnsupportedSettingsSchemaException.Missing(_settingsFilePath, HasLegacyShape(root));
        }

        if (versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out int declaredVersion))
        {
            throw UnsupportedSettingsSchemaException.Malformed(
                _settingsFilePath,
                versionElement.GetRawText(),
                HasLegacyShape(root));
        }

        return declaredVersion;
    }

    /// <summary>
    /// Reads a document stamped with the current version, refusing the mixed
    /// shape (a current stamp on legacy content would read as empty settings
    /// and lose its entries at the next save) with the remedy that fits the
    /// shape it carries - version 1, never the current one.
    /// </summary>
    private LauncherSettings ReadCurrentShape(JsonElement root, int declaredVersion)
    {
        if (HasLegacyShape(root))
        {
            throw UnsupportedSettingsSchemaException.Malformed(
                _settingsFilePath,
                declaredVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                legacyShape: true);
        }

        return DeserializeSettings(root);
    }

    /// <summary>
    /// True when the document carries the legacy <c>backends</c> array and no
    /// current <c>providers</c> array: the shape that may only ever be told
    /// to declare a migratable version (1), never the current one.
    /// </summary>
    private static bool HasLegacyShape(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("backends", out _)
        && !root.TryGetProperty("providers", out _);

    public void Save(LauncherSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        LauncherSettings stamped = settings with
        {
            SchemaVersion = LauncherSettings.CurrentSchemaVersion,
        };
        string json = JsonSerializer.Serialize(stamped, SettingsJsonContext.Default.LauncherSettings);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Reads a schemaVersion 1 or 2 file through the legacy read model and
    /// converts it to the current shape: the v1 plaintext-key sweep runs
    /// first (unchanged behavior), then the T020 mapping rules apply. The
    /// result is stamped with the current schema version. Also returns the
    /// bytes the rewrite should back up - for v1 the swept legacy shape, so
    /// the marker never becomes a second copy of a plaintext key; for v2
    /// (references only) the caller backs up the file as it stands. Nothing
    /// here writes to disk - that is <see cref="RewriteThroughBackupFlowAsync"/>'s job.
    /// </summary>
    private async Task<(LauncherSettings Settings, byte[]? MarkerBytes)> ConvertLegacyShape(
        JsonElement root,
        int declaredVersion,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618 // the legacy read model is deliberately obsolete (removed first release after v1)
        LegacySettings legacy = JsonSerializer.Deserialize(
            root,
            SettingsJsonContext.Default.LegacySettings)
            ?? new LegacySettings();

        byte[]? markerBytes = null;
        if (declaredVersion == 1)
        {
            // SchemaVersion 1 keeps flowing through the plaintext-key
            // migration first; schemaVersion 2 files already carry references.
            await SweepLegacyPlaintextKeys(legacy, cancellationToken).ConfigureAwait(false);

            // Back up the swept shape, not the plaintext the file still holds
            // on disk: a failed or crashed migration must not leave a
            // secret-bearing marker behind (no-secrets-in-JSON). Restoring
            // these bytes yields a valid v1 file whose keys are references,
            // which re-migrates idempotently.
            markerBytes = System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(legacy, SettingsJsonContext.Default.LegacySettings));
        }

        return (LegacySettings.ToLauncherSettings(legacy), markerBytes);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Rewrites the settings file through the BackupService flow (T003):
    /// backup the current state first (the explicit swept bytes for v1,
    /// otherwise the file as it stands), then write the converted shape
    /// through a temp file and an atomic move - a failed write leaves the old
    /// file intact and the error propagates to the caller - and finally
    /// commit the change so neither the launch-time restore nor the next
    /// SelfHeal replay can undo the migration. Idempotent by construction:
    /// only a legacy-shaped file ever reaches this path.
    /// </summary>
    private async Task RewriteThroughBackupFlowAsync(
        LauncherSettings converted,
        byte[]? markerBytes,
        CancellationToken cancellationToken)
    {
        if (markerBytes is not null)
        {
            await _backupService.BackupAsync(_settingsFilePath, markerBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _backupService.BackupAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
        }

        string tempPath = _settingsFilePath + MigrationTempSuffix;
        string json = JsonSerializer.Serialize(converted, SettingsJsonContext.Default.LauncherSettings);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _settingsFilePath, overwrite: true);

        // Commit semantics: the migrated file stands as written and its
        // marker is dropped, so neither the launch-time restore nor the next
        // SelfHeal replay can undo the migration. Scoped to this target only:
        // a shared BackupService may still hold a launch's pending config
        // restores, which keep their obligation.
        _backupService.CommitBackup(_settingsFilePath);
    }

#pragma warning disable CS0618 // sweeps the deliberately-obsolete read model (removed first release after v1)
    /// <summary>
    /// Moves a v1 file's plaintext API keys into the credential store,
    /// replacing each with its opaque <c>hgkey:&lt;name&gt;</c> reference in
    /// place, before conversion maps the entries onto providers.
    /// </summary>
    private async Task SweepLegacyPlaintextKeys(LegacySettings legacy, CancellationToken cancellationToken)
    {
        for (int index = 0; index < legacy.Backends.Count; index++)
        {
            LegacyBackend backend = legacy.Backends[index];
            if (backend.ApiKey is null || KeyReference.IsReference(backend.ApiKey))
            {
                continue;
            }

            string reference = KeyReference.ForProvider(backend.Name);

            ISecureKeyStore keyStore = _keyStore ??= SecureKeyStoreFactory.Create(_settingsDirectory);
            await keyStore.StoreAsync(reference, backend.ApiKey, cancellationToken).ConfigureAwait(false);

            legacy.Backends[index] = backend with { ApiKey = reference };
        }
    }
#pragma warning restore CS0618

    private static LauncherSettings DeserializeSettings(JsonElement root) =>
        JsonSerializer.Deserialize<LauncherSettings>(root, SettingsJsonContext.Default.LauncherSettings)
        ?? new LauncherSettings();

    public static string GetPlatformConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "HarnessGateway");
        }

        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "HarnessGateway");
        }

        // Linux and others
        string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig))
        {
            return Path.Combine(xdgConfig, "HarnessGateway");
        }

        string linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(linuxHome, ".config", "HarnessGateway");
    }
}

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
