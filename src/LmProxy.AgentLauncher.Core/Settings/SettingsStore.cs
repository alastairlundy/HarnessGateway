using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace LmProxy.AgentLauncher.Core.Settings;

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
        var directory = settingsDirectory ?? GetPlatformConfigDirectory();
        _settingsFilePath = Path.Combine(directory, "lmproxy-launcher.json");
        _configuration = BuildConfiguration(directory);
    }

    public string SettingsFilePath => _settingsFilePath;

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new LauncherSettings();
        }

        var json = File.ReadAllText(_settingsFilePath);
        var settings = JsonSerializer.Deserialize<LauncherSettings>(json, SerializerOptions);

        return settings ?? new LauncherSettings();
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public string? GetEnvironmentOverride(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }

    private static IConfiguration BuildConfiguration(string settingsDirectory)
    {
        var builder = new ConfigurationBuilder();

        var settingsFile = Path.Combine(settingsDirectory, "lmproxy-launcher.json");
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "LmProxy");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "LmProxy");
        }

        // Linux and others
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig))
        {
            return Path.Combine(xdgConfig, "lmproxy");
        }

        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(linuxHome, ".config", "lmproxy");
    }
}
