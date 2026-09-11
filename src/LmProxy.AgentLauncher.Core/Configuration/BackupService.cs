using System.Text.Json;

namespace LmProxy.AgentLauncher.Core.Configuration;

public sealed record BackupRecord(
    string TargetPath,
    byte[]? OriginalBytes,
    bool Persisted);

public sealed class BackupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _backupsDirectory;
    private readonly List<BackupRecord> _currentLaunchBackups = [];

    public BackupService(string backupsDirectory)
    {
        _backupsDirectory = backupsDirectory;
        Directory.CreateDirectory(_backupsDirectory);
    }

    public IReadOnlyList<BackupRecord> CurrentLaunchBackups => _currentLaunchBackups;

    public async Task BackupAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        if (_currentLaunchBackups.Any(b => b.TargetPath == targetPath))
        {
            return;
        }

        byte[]? originalBytes = null;
        if (File.Exists(targetPath))
        {
            originalBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
        }

        var record = new BackupRecord(targetPath, originalBytes, Persisted: false);
        _currentLaunchBackups.Add(record);

        var backupPath = GetBackupPath(targetPath);
        var json = JsonSerializer.Serialize(record, SerializerOptions);
        await File.WriteAllTextAsync(backupPath, json, cancellationToken);
    }

    public async Task RestoreOnExitAsync(bool persistConfig, CancellationToken cancellationToken = default)
    {
        foreach (var record in _currentLaunchBackups)
        {
            if (persistConfig && record.Persisted)
            {
                var backupPath = GetBackupPath(record.TargetPath);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                continue;
            }

            await RestoreRecordAsync(record, cancellationToken);

            var backupFilePath = GetBackupPath(record.TargetPath);
            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
            }
        }

        _currentLaunchBackups.Clear();
    }

    public async Task SelfHealAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_backupsDirectory))
        {
            return;
        }

        var backupFiles = Directory.GetFiles(_backupsDirectory, "*.json");
        foreach (var backupFile in backupFiles)
        {
            var json = await File.ReadAllTextAsync(backupFile, cancellationToken);
            var record = JsonSerializer.Deserialize<BackupRecord>(json, SerializerOptions);
            if (record is null)
            {
                continue;
            }

            if (record.Persisted)
            {
                continue;
            }

            await RestoreRecordAsync(record, cancellationToken);
            File.Delete(backupFile);
        }
    }

    public void RegisterSignalHandlers(CancellationTokenSource linkedCts)
    {
        var restored = false;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (!restored)
            {
                restored = true;
                linkedCts.Cancel();
            }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!restored)
            {
                restored = true;
            }
        };
    }

    private async Task RestoreRecordAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(record.TargetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (record.OriginalBytes is not null)
        {
            await File.WriteAllBytesAsync(record.TargetPath, record.OriginalBytes, cancellationToken);
        }
        else if (File.Exists(record.TargetPath))
        {
            File.Delete(record.TargetPath);
        }
    }

    private string GetBackupPath(string targetPath)
    {
        var sanitized = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(targetPath))
            .Replace("/", "_")
            .Replace("\\", "_");
        return Path.Combine(_backupsDirectory, $"{sanitized}.json");
    }
}
