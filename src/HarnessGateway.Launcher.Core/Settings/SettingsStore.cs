using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HarnessGateway.Launcher.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;
    private readonly IConfiguration _configuration;

    public SettingsStore(string? settingsDirectory = null)
    {
        string directory = settingsDirectory ?? GetPlatformConfigDirectory();
        _settingsFilePath = Path.Combine(directory, "HarnessGateway-launcher.json");
        _configuration = BuildConfiguration(directory);
    }

    public string SettingsFilePath => _settingsFilePath;

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new LauncherSettings();
        }

        string json = File.ReadAllText(_settingsFilePath);
        LauncherSettings? settings = JsonSerializer.Deserialize<LauncherSettings>(json, SerializerOptions);

        return settings ?? new LauncherSettings();
    }

    public void Save(LauncherSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public string? GetEnvironmentOverride(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }

    private static IConfiguration BuildConfiguration(string settingsDirectory)
    {
        ConfigurationBuilder builder = new ConfigurationBuilder();

        string settingsFile = Path.Combine(settingsDirectory, "HarnessGateway-launcher.json");
        if (File.Exists(settingsFile))
        {
            builder.AddJsonFile(settingsFile, optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables();

        return builder.Build();
    }

    private static string GetPlatformConfigDirectory()
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
