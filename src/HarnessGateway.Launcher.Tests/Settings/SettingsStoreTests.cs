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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir);

            var settings = new LauncherSettings
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
            var loaded = store.Load();

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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir);

            var loaded = store.Load();

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
    public async Task Environment_variable_overrides_file_value()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "file-backend",
                        BaseUri = new Uri("https://api.file.com"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "file-backend",
                SchemaVersion = 1
            };

            var store = new SettingsStore(tempDir);
            store.Save(settings);

            Environment.SetEnvironmentVariable("HarnessGateway_DEFAULT_BACKEND", "env-override", EnvironmentVariableTarget.Process);
            try
            {
                var overrideValue = store.GetEnvironmentOverride("HarnessGateway_DEFAULT_BACKEND");
                await Assert.That(overrideValue).IsEqualTo("env-override");
            }
            finally
            {
                Environment.SetEnvironmentVariable("HarnessGateway_DEFAULT_BACKEND", null, EnvironmentVariableTarget.Process);
            }
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var keyStore = new FakeSecureKeyStore();
            var store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawV1Json("sk-legacy-plaintext"));

            var loaded = store.Load();

            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Backends[0].ApiKey).IsEqualTo("hgkey:default");

            var stored = await keyStore.RetrieveAsync("hgkey:default");
            await Assert.That(stored).IsEqualTo("sk-legacy-plaintext");

            var fileBytes = File.ReadAllText(store.SettingsFilePath);
            var expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var keyStore = new FakeSecureKeyStore();
            var store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawV1Json("hgkey:default"));

            var loaded = store.Load();

            await Assert.That(loaded.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
            await Assert.That(loaded.Backends[0].ApiKey).IsEqualTo("hgkey:default");
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);

            var fileBytes = File.ReadAllText(store.SettingsFilePath);
            var expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var keyStore = new FakeSecureKeyStore();
            var store = new SettingsStore(tempDir, keyStore);
            WriteRawSettings(store, RawVersionJson(3));
            var before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                store.Load();
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, """{ "backends": [], "defaultBackend": "" }""");
            var before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                store.Load();
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(
                store,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": "x" }""");
            var before = File.ReadAllText(store.SettingsFilePath);

            Exception? caught = null;
            try
            {
                store.Load();
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir, new FakeSecureKeyStore());
            WriteRawSettings(store, RawVersionJson(0));

            Exception? caught = null;
            try
            {
                store.Load();
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SettingsStore(tempDir);
            var settings = new LauncherSettings
            {
                DefaultBackend = string.Empty,
                SchemaVersion = 1
            };

            store.Save(settings);

            using var document = JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            var writtenVersion = document.RootElement.GetProperty("schemaVersion").GetInt32();
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
