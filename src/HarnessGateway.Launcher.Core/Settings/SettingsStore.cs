using System.Text.Json;
using HarnessGateway.Launcher.Core.Settings.Security;
using Microsoft.Extensions.Configuration;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed class SettingsStore
{
    private const string SchemaVersionPropertyName = "schemaVersion";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly IConfiguration _configuration;
    private ISecureKeyStore? _keyStore;

    public SettingsStore(string? settingsDirectory = null, ISecureKeyStore? keyStore = null)
    {
        string directory = settingsDirectory ?? GetPlatformConfigDirectory();
        _settingsDirectory = directory;
        _settingsFilePath = Path.Combine(directory, "HarnessGateway-launcher.json");
        _configuration = BuildConfiguration(directory);
        _keyStore = keyStore;
    }

    public string SettingsFilePath => _settingsFilePath;

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new();
        }

        string json = File.ReadAllText(_settingsFilePath);
        int declaredVersion = ReadDeclaredVersion(json);

        if (declaredVersion == LauncherSettings.CurrentSchemaVersion)
        {
            return DeserializeSettings(json);
        }

        if (declaredVersion == 1)
        {
            LauncherSettings settings = DeserializeSettings(json);
            SweepLegacyPlaintextKeys(settings);

            LauncherSettings migrated = settings with
            {
                SchemaVersion = LauncherSettings.CurrentSchemaVersion
            };
            Save(migrated);
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
            declaredVersion.ToString());
    }

    public void Save(LauncherSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        LauncherSettings stamped = settings with
        {
            SchemaVersion = LauncherSettings.CurrentSchemaVersion
        };
        string json = JsonSerializer.Serialize(stamped, SerializerOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public string? GetEnvironmentOverride(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }

    private int ReadDeclaredVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(SchemaVersionPropertyName, out JsonElement versionElement))
        {
            throw UnsupportedSettingsSchemaException.Missing(_settingsFilePath);
        }

        if (versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out int declaredVersion))
        {
            throw UnsupportedSettingsSchemaException.Malformed(
                _settingsFilePath,
                versionElement.GetRawText());
        }

        return declaredVersion;
    }

    private void SweepLegacyPlaintextKeys(LauncherSettings settings)
    {
        for (int index = 0; index < settings.Backends.Count; index++)
        {
            Backend backend = settings.Backends[index];
            if (backend.ApiKey is null || KeyReference.IsReference(backend.ApiKey))
            {
                continue;
            }

            string reference = KeyReference.ForBackend(backend.Name);

            ISecureKeyStore keyStore = _keyStore ??= SecureKeyStoreFactory.Create(_settingsDirectory);
            keyStore
                .StoreAsync(reference, backend.ApiKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            settings.Backends[index] = backend with { ApiKey = reference };
        }
    }

    private static LauncherSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<LauncherSettings>(json, SerializerOptions)
        ?? new LauncherSettings();

    private static IConfiguration BuildConfiguration(string settingsDirectory)
    {
        ConfigurationBuilder builder = new();

        string settingsFile = Path.Combine(settingsDirectory, "HarnessGateway-launcher.json");
        if (File.Exists(settingsFile))
        {
            builder.AddJsonFile(settingsFile, optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables();

        return builder.Build();
    }

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
/// the T039 phrase "unsupported settings schema version", the file path, the
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
    public static UnsupportedSettingsSchemaException Missing(string path) =>
        new(
            $"The settings file '{path}' declares no schemaVersion field: " +
            $"unsupported settings schema version (supported: 1 through {LauncherSettings.CurrentSchemaVersion}). " +
            $"Add \"schemaVersion\" - 1 for a file written by an older HarnessGateway, " +
            $"{LauncherSettings.CurrentSchemaVersion} for a current one - or delete the file to start fresh. " +
            "The file was left untouched.");

    /// <summary>The schemaVersion field is present but is not a supported version number.</summary>
    public static UnsupportedSettingsSchemaException Malformed(string path, string declaredValue) =>
        new(
            $"The settings file '{path}' declares schemaVersion {declaredValue}: " +
            $"unsupported settings schema version (supported: 1 through {LauncherSettings.CurrentSchemaVersion}). " +
            $"Set the field to 1 for a file written by an older HarnessGateway or to " +
            $"{LauncherSettings.CurrentSchemaVersion} for a current one, or delete the file to start fresh. " +
            "The file was left untouched.");

    /// <summary>The file was written by a newer build than this one.</summary>
    public static UnsupportedSettingsSchemaException Newer(string path, int declared, int supported) =>
        new(
            $"The settings file '{path}' declares schemaVersion {declared} but this build " +
            $"supports up to {supported}: unsupported settings schema version. " +
            $"Upgrade HarnessGateway to a build that understands version {declared}; " +
            "this build will not read or rewrite the file. The file was left untouched.");
}
