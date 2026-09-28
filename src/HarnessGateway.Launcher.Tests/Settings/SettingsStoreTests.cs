using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Settings;

public class SettingsStoreTests
{
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
            await Assert.That(loaded.SchemaVersion).IsEqualTo(1);
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
            await Assert.That(loaded.SchemaVersion).IsEqualTo(1);
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
}
