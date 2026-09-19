using HarnessGateway.Launcher.Core.Configuration;

namespace HarnessGateway.Launcher.Tests.Configuration;

public class BackupServiceTests
{
    [Test]
    public async Task Backup_captures_original_bytes()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original content");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);

            await Assert.That(service.CurrentLaunchBackups).Count().IsEqualTo(1);
            await Assert.That(service.CurrentLaunchBackups[0].TargetPath).IsEqualTo(targetFile);
            await Assert.That(service.CurrentLaunchBackups[0].OriginalBytes)
                .IsEquivalentTo(System.Text.Encoding.UTF8.GetBytes("original content"));
            await Assert.That(service.CurrentLaunchBackups[0].Persisted).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Backup_does_not_duplicate_same_target()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "content");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);
            await service.BackupAsync(targetFile);

            await Assert.That(service.CurrentLaunchBackups).Count().IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Backup_records_null_bytes_for_missing_file()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "missing.txt");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);

            await Assert.That(service.CurrentLaunchBackups[0].OriginalBytes).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Backup_persists_record_to_disk()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "hello");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);

            var backupFiles = Directory.GetFiles(backupDir, "*.json");
            await Assert.That(backupFiles).Count().IsEqualTo(1);

            var json = await File.ReadAllTextAsync(backupFiles[0]);
            var record = System.Text.Json.JsonSerializer.Deserialize<BackupRecord>(json);
            await Assert.That(record).IsNotNull();
            await Assert.That(record!.TargetPath).IsEqualTo(targetFile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RestoreOnExit_restores_original_content()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);
            await File.WriteAllTextAsync(targetFile, "modified");

            await service.RestoreOnExitAsync(persistConfig: false);

            var restored = await File.ReadAllTextAsync(targetFile);
            await Assert.That(restored).IsEqualTo("original");
            await Assert.That(service.CurrentLaunchBackups).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task RestoreOnExit_deletes_backup_files()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "content");

            var backupDir = Path.Combine(tempDir, "backups");
            var service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);
            await service.RestoreOnExitAsync(persistConfig: false);

            var backupFiles = Directory.Exists(backupDir) ? Directory.GetFiles(backupDir, "*.json") : [];
            await Assert.That(backupFiles).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SelfHeal_restores_unmarked_leftover_backups()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original");

            var backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            var record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("original"), Persisted: false);
            var backupPath = Path.Combine(backupDir, "test.json");
            var json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            await File.WriteAllTextAsync(targetFile, "corrupted");

            var service = new BackupService(backupDir);
            await service.SelfHealAsync();

            var restored = await File.ReadAllTextAsync(targetFile);
            await Assert.That(restored).IsEqualTo("original");
            await Assert.That(File.Exists(backupPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SelfHeal_skips_persisted_leftover_backups()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "current");

            var backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            var record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("old"), Persisted: true);
            var backupPath = Path.Combine(backupDir, "test.json");
            var json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            var service = new BackupService(backupDir);
            await service.SelfHealAsync();

            var content = await File.ReadAllTextAsync(targetFile);
            await Assert.That(content).IsEqualTo("current");
            await Assert.That(File.Exists(backupPath)).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SelfHeal_restores_missing_file_from_backup()
    {
        var tempDir = CreateTempDir();
        try
        {
            var targetFile = Path.Combine(tempDir, "target.txt");

            var backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            var record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("was here"), Persisted: false);
            var backupPath = Path.Combine(backupDir, "test.json");
            var json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            var service = new BackupService(backupDir);
            await service.SelfHealAsync();

            var restored = await File.ReadAllTextAsync(targetFile);
            await Assert.That(restored).IsEqualTo("was here");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
