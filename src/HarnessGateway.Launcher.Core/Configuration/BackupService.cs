using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarnessGateway.Launcher.Core.Configuration;

public sealed class BackupService
{
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
        if (_currentLaunchBackups.Exists(b => string.Equals(b.TargetPath, targetPath, StringComparison.Ordinal)))
        {
            return;
        }

        byte[]? originalBytes = null;
        if (File.Exists(targetPath))
        {
            originalBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken).ConfigureAwait(false);
        }

        BackupRecord record = new(targetPath, originalBytes, Persisted: false);
        _currentLaunchBackups.Add(record);

        string backupPath = GetBackupPath(targetPath);
        string json = JsonSerializer.Serialize(record, BackupJsonContext.Default.BackupRecord);
        await File.WriteAllTextAsync(backupPath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreOnExitAsync(bool persistConfig, CancellationToken cancellationToken = default)
    {
        foreach (BackupRecord record in _currentLaunchBackups)
        {
            if (persistConfig)
            {
                string backupPath = GetBackupPath(record.TargetPath);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                continue;
            }

            await RestoreRecordAsync(record, cancellationToken).ConfigureAwait(false);

            string backupFilePath = GetBackupPath(record.TargetPath);
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

        string[] backupFiles = Directory.GetFiles(_backupsDirectory, "*.json");
        foreach (string backupFile in backupFiles)
        {
            string json = await File.ReadAllTextAsync(backupFile, cancellationToken).ConfigureAwait(false);
            BackupRecord? record = JsonSerializer.Deserialize<BackupRecord>(json, BackupJsonContext.Default.BackupRecord);
            if (record is null)
            {
                continue;
            }

            if (record.Persisted)
            {
                continue;
            }

            await RestoreRecordAsync(record, cancellationToken).ConfigureAwait(false);
            File.Delete(backupFile);
        }
    }

    private async Task RestoreRecordAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(record.TargetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (record.OriginalBytes is not null)
        {
            await File.WriteAllBytesAsync(record.TargetPath, record.OriginalBytes, cancellationToken).ConfigureAwait(false);
        }
        else if (File.Exists(record.TargetPath))
        {
            File.Delete(record.TargetPath);
        }
    }

    private string GetBackupPath(string targetPath)
    {
        // SHA-256 hex: a fixed 64 path-safe chars ([0-9A-F]). The previous
        // raw-hex-of-path scheme doubled the target path's length, which
        // broke the platform's per-filename limit (255 chars) for longer
        // targets such as the settings file under a deep temp directory.
        // The filename only has to round-trip within one process (SelfHeal
        // discovers markers by globbing *.json and reads the target out of
        // the record's content), so a stable hash is enough.
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(targetPath)));
        return Path.Combine(_backupsDirectory, $"{name}.json");
    }
}