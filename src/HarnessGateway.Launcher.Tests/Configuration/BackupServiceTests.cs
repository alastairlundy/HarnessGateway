using HarnessGateway.Launcher.Core.Configuration;

namespace HarnessGateway.Launcher.Tests.Configuration;

public class BackupServiceTests
{
    [Test]
    public async Task Backup_captures_original_bytes()
    {
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original content");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "content");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "missing.txt");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "hello");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);

            string[] backupFiles = Directory.GetFiles(backupDir, "*.json");
            await Assert.That(backupFiles).Count().IsEqualTo(1);

            string json = await File.ReadAllTextAsync(backupFiles[0]);
            BackupRecord? record = System.Text.Json.JsonSerializer.Deserialize<BackupRecord>(json);
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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);
            await File.WriteAllTextAsync(targetFile, "modified");

            await service.RestoreOnExitAsync(persistConfig: false);

            string restored = await File.ReadAllTextAsync(targetFile);
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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "content");

            string backupDir = Path.Combine(tempDir, "backups");
            BackupService service = new BackupService(backupDir);

            await service.BackupAsync(targetFile);
            await service.RestoreOnExitAsync(persistConfig: false);

            string[] backupFiles = Directory.Exists(backupDir) ? Directory.GetFiles(backupDir, "*.json") : [];
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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "original");

            string backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            BackupRecord record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("original"), Persisted: false);
            string backupPath = Path.Combine(backupDir, "test.json");
            string json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            await File.WriteAllTextAsync(targetFile, "corrupted");

            BackupService service = new BackupService(backupDir);
            await service.SelfHealAsync();

            string restored = await File.ReadAllTextAsync(targetFile);
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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");
            await File.WriteAllTextAsync(targetFile, "current");

            string backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            BackupRecord record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("old"), Persisted: true);
            string backupPath = Path.Combine(backupDir, "test.json");
            string json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            BackupService service = new BackupService(backupDir);
            await service.SelfHealAsync();

            string content = await File.ReadAllTextAsync(targetFile);
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
        string tempDir = CreateTempDir();
        try
        {
            string targetFile = Path.Combine(tempDir, "target.txt");

            string backupDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupDir);

            BackupRecord record = new BackupRecord(targetFile, System.Text.Encoding.UTF8.GetBytes("was here"), Persisted: false);
            string backupPath = Path.Combine(backupDir, "test.json");
            string json = System.Text.Json.JsonSerializer.Serialize(record);
            await File.WriteAllTextAsync(backupPath, json);

            BackupService service = new BackupService(backupDir);
            await service.SelfHealAsync();

            string restored = await File.ReadAllTextAsync(targetFile);
            await Assert.That(restored).IsEqualTo("was here");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
