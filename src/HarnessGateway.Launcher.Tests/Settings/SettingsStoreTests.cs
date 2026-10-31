using System.Text.Json;
using HarnessGateway.Launcher.Core.Backends;
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

    [Test]
    public async Task Round_trip_persists_and_loads_settings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);

            LauncherSettings settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "test-backend",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        DefaultModel = "gpt-4",
                        ApiKey = "hgkey:test-backend"
                    }
                ],
                DefaultBackend = "test-backend",
                SchemaVersion = 1
            };

            store.Save(settings);
            LauncherSettings loaded = await store.LoadAsync();

            await Assert.That(loaded.DefaultBackend).IsEqualTo("test-backend");
            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Backends).Count().IsEqualTo(1);
            await Assert.That(loaded.Backends[0].Name).IsEqualTo("test-backend");
            await Assert.That(loaded.Backends[0].BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(loaded.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(loaded.Backends[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(loaded.Backends[0].ApiKey).IsEqualTo("hgkey:test-backend");
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

            await Assert.That(loaded.DefaultBackend).IsEmpty();
            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Backends).IsEmpty();
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
            await Assert.That(loaded.Backends[0].ApiKey).IsEqualTo("hgkey:default");

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
            await Assert.That(loaded.Backends[0].ApiKey).IsEqualTo("hgkey:default");
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
            WriteRawSettings(store, RawVersionJson(3));
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
    public async Task Save_stamps_the_current_schema_version_onto_the_file()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            SettingsStore store = new SettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
            {
                DefaultBackend = string.Empty,
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
}
