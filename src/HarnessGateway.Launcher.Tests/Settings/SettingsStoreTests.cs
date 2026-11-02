using System.Text.Json;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Settings;

public class SettingsStoreTests
{
    private static void WriteRawSettings(SettingsStore store, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
        File.WriteAllText(store.SettingsFilePath, json);
    }

    private static string RawV1Json(string apiKey) =>
        $$"""
        {
          "backends": [
            {
              "name": "default",
              "baseUri": "https://api.example.com/v1",
              "protocol": 0,
              "apiKey": "{{apiKey}}"
            }
          ],
          "defaultBackend": "default",
          "schemaVersion": 1
        }
        """;

    private static string RawVersionJson(int schemaVersion) =>
        $$"""
        {
          "backends": [],
          "defaultBackend": "",
          "schemaVersion": {{schemaVersion}}
        }
        """;

    private static string RawV2Json(string providerName) =>
        $$"""
        {
          "backends": [
            {
              "name": "{{providerName}}",
              "baseUri": "https://api.{{providerName}}.example/v1",
              "protocol": 0,
              "defaultModel": "gpt-4",
              "apiKey": "hgkey:{{providerName}}"
            }
          ],
          "defaultBackend": "{{providerName}}",
          "schemaVersion": 2
        }
        """;

    private static string CurrentJson(string providerName) =>
        $$"""
        {
          "providers": [
            {
              "name": "{{providerName}}",
              "baseUri": "https://api.{{providerName}}.example/v1",
              "protocol": 0
            }
          ],
          "defaultProvider": "{{providerName}}",
          "schemaVersion": 3
        }
        """;

    [Test]
    public async Task Round_trip_persists_and_loads_settings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);

            LauncherSettings settings = new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "test-backend",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                        DefaultModel = "gpt-4",
                        ApiKey = "hgkey:test-backend"
                    }
                ],
                DefaultProvider = "test-backend",
                SchemaVersion = 1
            };

            store.Save(settings);
            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.DefaultProvider).IsEqualTo("test-backend");
            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Providers).Count().IsEqualTo(1);
            await Assert.That(loaded.Providers[0].Name).IsEqualTo("test-backend");
            await Assert.That(loaded.Providers[0].BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(loaded.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(loaded.Providers[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(loaded.Providers[0].ApiKey).IsEqualTo("hgkey:test-backend");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_returns_empty_settings_when_no_file_exists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);

            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.DefaultProvider).IsEmpty();
            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Providers).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_migrates_a_v1_plaintext_key_to_a_store_reference()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawV1Json("sk-legacy-plaintext"));

            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Providers[0].ApiKey).IsEqualTo("hgkey:default");

            string stored = await keyStore.RetrieveAsync("hgkey:default");
            await Assert.That(stored).IsEqualTo("sk-legacy-plaintext");

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            string expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
            await Assert.That(fileBytes.Contains(expectedStamp)).IsTrue();
            await Assert.That(fileBytes.Contains("hgkey:default")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-legacy-plaintext")).IsFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_v1_with_only_references_bumps_the_version_without_touching_the_store()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawV1Json("hgkey:default"));

            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Providers[0].ApiKey).IsEqualTo("hgkey:default");
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            string expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
            await Assert.That(fileBytes.Contains(expectedStamp)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_newer_version_throws_a_typed_refusal_and_leaves_the_file_untouched()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawVersionJson(LauncherSettings.CurrentSchemaVersion + 1));
            string before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                await store.LoadAsync();

            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("unsupported settings schema version");
            await Assert.That(caught.Message).Contains(store.SettingsFilePath);
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_missing_schema_version_throws_a_typed_refusal()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, """{ "backends": [], "defaultBackend": "" }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                await store.LoadAsync();

            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("unsupported settings schema version");
            await Assert.That(caught.Message).Contains("delete the file");
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_non_integer_schema_version_throws_a_typed_refusal()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(
                store,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": "x" }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                await store.LoadAsync();

            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("unsupported settings schema version");
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_zero_schema_version_throws_a_typed_refusal()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, RawVersionJson(0));

            Exception? caught = null;
            try
            {
                await store.LoadAsync();

            }
            catch (UnsupportedSettingsSchemaException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("unsupported settings schema version");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Save_writes_the_current_provider_shape_with_no_backend_spellings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                    }
                ],
                DefaultProvider = "work",
            };

            store.Save(settings);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            JsonElement root = document.RootElement;

            // The single schemaVersion bump for this migration is part of the
            // contract: 3 is pinned literally, not read back from the constant
            // the file was just stamped with.
            await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(3);
            await Assert.That(root.GetProperty("defaultProvider").GetString()).IsEqualTo("work");
            await Assert.That(root.GetProperty("providers").GetArrayLength()).IsEqualTo(1);
            await Assert.That(root.TryGetProperty("backends", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("defaultBackend", out _)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Save_stamps_the_current_schema_version_onto_the_file()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
            {
                DefaultProvider = string.Empty,
                SchemaVersion = 1
            };

            store.Save(settings);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            int writtenVersion = document.RootElement.GetProperty("schemaVersion").GetInt32();
            await Assert.That(writtenVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_converts_a_v2_file_to_providers_and_rewrites_through_the_backup_flow()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            WriteRawSettings(store, RawV2Json("work"));

            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.DefaultProvider).IsEqualTo("work");
            await Assert.That(loaded.Providers).Count().IsEqualTo(1);
            await Assert.That(loaded.Providers[0].Name).IsEqualTo("work");
            await Assert.That(loaded.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(loaded.Providers[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(loaded.Providers[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(loaded.Providers[0].Models).IsEmpty();

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            JsonElement root = document.RootElement;
            await Assert.That(root.GetProperty("schemaVersion").GetInt32())
                .IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(root.GetProperty("providers").GetArrayLength()).IsEqualTo(1);
            await Assert.That(root.TryGetProperty("backends", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("defaultBackend", out _)).IsFalse();

            // The rewrite committed its backup: no marker survives a
            // successful conversion, so the next SelfHeal has nothing to
            // replay and the migrated file is durable.
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"), "*.json")).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_second_read_after_conversion_leaves_the_file_byte_identical()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            WriteRawSettings(store, RawV2Json("work"));

            LauncherSettings first = await store.LoadAsync();
            string converged = File.ReadAllText(store.SettingsFilePath);
            DateTime convergedTimestamp = File.GetLastWriteTimeUtc(store.SettingsFilePath);

            LauncherSettings second = await store.LoadAsync();

            await Assert.That(first.DefaultProvider).IsEqualTo("work");
            await Assert.That(second.DefaultProvider).IsEqualTo("work");
            await Assert.That(second.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);

            // Converged once: the second read neither rewrites the bytes nor
            // touches the file at all.
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(converged);
            await Assert.That(File.GetLastWriteTimeUtc(store.SettingsFilePath))
                .IsEqualTo(convergedTimestamp);
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"), "*.json")).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_failed_rewrite_leaves_the_old_file_intact_and_surfaces_the_error()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            WriteRawSettings(store, RawV2Json("work"));
            string before = File.ReadAllText(store.SettingsFilePath);

            // The rewrite writes through a temp file; turning that path into a
            // directory makes the write fail after the backup was taken, on
            // every platform, without relying on file permissions.
            Directory.CreateDirectory(store.SettingsFilePath + ".migrating");

            Exception? caught = null;
            try
            {
                await store.LoadAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);

            // Backup-before-write held: the marker the failed rewrite took is
            // still on disk, ready for the next SelfHeal replay.
            await Assert.That(Directory.GetFiles(Path.Combine(tempDir, "backups"), "*.json"))
                .Count().IsEqualTo(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task Load_replays_leftover_backups_before_converting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            WriteRawSettings(store, CurrentJson("current"));

            // A leftover uncommitted marker from a crashed earlier run: its
            // original bytes are a legacy v2 file naming "replayed".
            string backupsDir = Path.Combine(tempDir, "backups");
            Directory.CreateDirectory(backupsDir);
            BackupRecord leftover = new(
                store.SettingsFilePath,
                System.Text.Encoding.UTF8.GetBytes(RawV2Json("replayed")),
                Persisted: false);
            string markerPath = Path.Combine(backupsDir, "leftover.json");
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(leftover));

            LauncherSettings loaded = await store.LoadAsync();

            // The replay ran first: the conversion saw the replayed legacy
            // bytes, not the file's original current-shape content.
            await Assert.That(loaded.DefaultProvider).IsEqualTo("replayed");
            await Assert.That(loaded.Providers).Count().IsEqualTo(1);
            await Assert.That(loaded.Providers[0].Name).IsEqualTo("replayed");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            await Assert.That(document.RootElement.GetProperty("defaultProvider").GetString())
                .IsEqualTo("replayed");
            await Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32())
                .IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(File.Exists(markerPath)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
