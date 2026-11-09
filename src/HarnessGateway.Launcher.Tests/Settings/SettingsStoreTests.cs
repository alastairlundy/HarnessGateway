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
            // A versionless file that carries the legacy backends shape may
            // only ever be told to declare 1 - recommending the current
            // version would read it as an empty current-shape file and lose
            // its entries at the next save.
            await Assert.That(caught.Message).Contains("legacy backends shape");
            await Assert.That(caught.Message.Contains("3 for a current one")).IsFalse();
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
    public async Task Load_missing_schema_version_on_a_current_shape_file_keeps_the_current_version_advice()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, """{ "providers": [], "defaultProvider": "" }""");
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
            await Assert.That(caught.Message).Contains("3 for a current one");
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
    public async Task Load_mixed_shape_file_declaring_current_version_is_refused_not_read_as_empty()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, RawV2Json("work").Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3"));
            string before = File.ReadAllText(store.SettingsFilePath);

            // Declaring the current version while carrying the legacy shape
            // would otherwise deserialize to empty settings - silent loss of
            // the saved entries. It must fail like any other unreadable file.
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
            await Assert.That(caught.Message).Contains("legacy backends shape");
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
    public async Task Load_v1_failed_rewrite_marker_carries_no_plaintext_key()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawV1Json("sk-legacy-plaintext"));
            string before = File.ReadAllText(store.SettingsFilePath);

            // Fail the rewrite after the backup was taken (see
            // Load_failed_rewrite...): the marker then outlives the failure,
            // so it is the copy that must never hold the secret.
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

            // The user's own file stays exactly as it was - plaintext and all.
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
            await Assert.That(before).Contains("sk-legacy-plaintext");

            // The sweep still ran: the secret lives in the credential store.
            string stored = await keyStore.RetrieveAsync("hgkey:default");
            await Assert.That(stored).IsEqualTo("sk-legacy-plaintext");

            // The backup marker - a second copy in JSON that outlives the
            // failed rewrite - carries the swept shape, never the secret
            // (no-secrets-in-JSON invariant).
            string[] markers = Directory.GetFiles(Path.Combine(tempDir, "backups"), "*.json");
            await Assert.That(markers).Count().IsEqualTo(1);
            BackupRecord? record = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(markers[0]),
                BackupJsonContext.Default.BackupRecord);
            await Assert.That(record).IsNotNull();
            await Assert.That(record!.OriginalBytes).IsNotNull();

            string markerJson = System.Text.Encoding.UTF8.GetString(record.OriginalBytes!);
            await Assert.That(markerJson.Contains("sk-legacy-plaintext")).IsFalse();
            await Assert.That(markerJson.Contains("hgkey:default")).IsTrue();
            await Assert.That(markerJson.Contains("\"schemaVersion\": 1")).IsTrue();
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
    public async Task Migration_commit_clears_only_its_own_pending_restore_records()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            string settingsDir = Path.Combine(tempDir, "settings");
            Directory.CreateDirectory(settingsDir);
            string backupsDir = Path.Combine(settingsDir, "backups");
            Directory.CreateDirectory(backupsDir);

            // The production wiring (Program.cs) shares ONE BackupService
            // between the store's load-time migration and the flow's
            // restore-pending config records.
            BackupService shared = new BackupService(backupsDir);
            SettingsStore store = new SettingsStore(
                settingsDir, new FakeSecureKeyStore(), shared);
            await File.WriteAllTextAsync(store.SettingsFilePath, RawV2Json("work"));

            // A pending config-action backup - the restore guarantee's
            // pre-exit state: original on disk, current in memory.
            string configPath = Path.Combine(tempDir, "harness-config.toml");
            await File.WriteAllTextAsync(configPath, "original");
            await shared.BackupAsync(configPath);
            await File.WriteAllTextAsync(configPath, "modified");

            // ...and must commit only its own: the pending config record
            // keeps its restore obligation, while the migration's own record
            // is gone. (The config marker file itself is consumed earlier -
            // SelfHeal replays any pre-existing marker at the start of Load -
            // which is the same recovery behavior production relies on.)
            LauncherSettings loaded = await store.LoadAsync();
            await Assert.That(loaded.DefaultProvider).IsEqualTo("work");

            await Assert.That(
                shared.CurrentLaunchBackups.Count(r =>
                    string.Equals(r.TargetPath, configPath, StringComparison.Ordinal)))
                .IsEqualTo(1);
            await Assert.That(
                shared.CurrentLaunchBackups.Count(r =>
                    string.Equals(r.TargetPath, store.SettingsFilePath, StringComparison.Ordinal)))
                .IsEqualTo(0);

            // The pending restore still executes for the config file, and no
            // marker survives: the migration's by its scoped commit, the
            // config's by that restore.
            await shared.RestoreOnExitAsync(persistConfig: false);
            await Assert.That(await File.ReadAllTextAsync(configPath)).IsEqualTo("original");
            await Assert.That(Directory.GetFiles(backupsDir, "*.json")).IsEmpty();

            // And the migrated settings file was never part of that restore.
            string settingsBytes = await File.ReadAllTextAsync(store.SettingsFilePath);
            await Assert.That(settingsBytes.Contains(
                $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}")).IsTrue();
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
