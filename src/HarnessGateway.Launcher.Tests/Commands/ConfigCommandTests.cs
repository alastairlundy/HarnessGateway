using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Commands;

public class ConfigCommandTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsStore CreateSettingsStore(string tempDir)
    {
        var settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        return new SettingsStore(settingsDir);
    }

    private static int InvokeConfig(SettingsStore store, string args)
    {
        var cmd = new ConfigCommand(store);
        var rootCmd = new RootCommand { cmd };
        var result = rootCmd.Parse(args);
        return result.Invoke();
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_default_backend_by_name()
    {
        var settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "other",
                    BaseUri = new Uri("https://other.example.com/v1"),
                    Protocol = ProtocolKind.Anthropic
                },
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };

        var backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("default");
        await Assert.That(backend.BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_first_when_no_default_set()
    {
        var settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "first",
                    BaseUri = new Uri("https://first.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = string.Empty
        };

        var backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("first");
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_null_when_no_backends()
    {
        var settings = new LauncherSettings();

        var backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNull();
    }

    [Test]
    public async Task UpdateBackend_updates_existing_backend()
    {
        var settings = new LauncherSettings
        {
            Backends =
            [
                new Backend
                {
                    Name = "default",
                    BaseUri = new Uri("https://old.example.com/v1"),
                    Protocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultBackend = "default"
        };

        var updated = new Backend
        {
            Name = "default",
            BaseUri = new Uri("https://new.example.com/v1"),
            Protocol = ProtocolKind.Anthropic
        };

        ConfigCommand.UpdateBackend(settings, updated);

        await Assert.That(settings.Backends).HasCount().EqualTo(1);
        await Assert.That(settings.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
        await Assert.That(settings.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
    }

    [Test]
    public async Task UpdateBackend_adds_new_backend_when_not_exists()
    {
        var settings = new LauncherSettings
        {
            Backends = [],
            DefaultBackend = "new"
        };

        var newBackend = new Backend
        {
            Name = "new",
            BaseUri = new Uri("https://new.example.com/v1"),
            Protocol = ProtocolKind.OpenAiChat
        };

        ConfigCommand.UpdateBackend(settings, newBackend);

        await Assert.That(settings.Backends).HasCount().EqualTo(1);
        await Assert.That(settings.Backends[0].Name).IsEqualTo("new");
    }

    [Test]
    public async Task Show_verb_returns_zero()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "test-key",
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            var exitCode = InvokeConfig(store, "config show");

            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Show_verb_no_backend_returns_zero()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config show");

            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultEndpoint_valid_protocol_persists()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-chat");

            await Assert.That(exitCode).IsEqualTo(0);

            var saved = store.Load();
            await Assert.That(saved.DefaultBackend).IsEqualTo("default");
            await Assert.That(saved.Backends).HasCount().EqualTo(1);
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://api.openai.com/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultEndpoint_invalid_protocol_returns_usage_error()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config set-default-endpoint https://api.example.com/v1 invalid-protocol");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultEndpoint_preserves_existing_key_and_model()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var existing = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://old.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "existing-key",
                        DefaultModel = "existing-model"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(existing);

            var exitCode = InvokeConfig(store, "config set-default-endpoint https://new.example.com/v1 anthropic");

            await Assert.That(exitCode).IsEqualTo(0);

            var saved = store.Load();
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("existing-key");
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("existing-model");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetSavedKey_persists_key_to_default_backend()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            var exitCode = InvokeConfig(store, "config set-saved-key my-secret-key");

            await Assert.That(exitCode).IsEqualTo(0);

            var saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("my-secret-key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetSavedKey_no_backend_returns_usage_error()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config set-saved-key my-key");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearSavedKey_removes_key_from_backend()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "old-key"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            var exitCode = InvokeConfig(store, "config clear-saved-key");

            await Assert.That(exitCode).IsEqualTo(0);

            var saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearSavedKey_no_backend_returns_usage_error()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config clear-saved-key");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultModel_persists_model_to_backend()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            var exitCode = InvokeConfig(store, "config set-default-model claude-sonnet-5");

            await Assert.That(exitCode).IsEqualTo(0);

            var saved = store.Load();
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("claude-sonnet-5");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultModel_no_backend_returns_usage_error()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);
            var exitCode = InvokeConfig(store, "config set-default-model gpt-4");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultEndpoint_all_protocols_persist()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = CreateSettingsStore(tempDir);

            InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-chat");
            var saved1 = store.Load();
            await Assert.That(saved1.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);

            InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-responses");
            var saved2 = store.Load();
            await Assert.That(saved2.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);

            InvokeConfig(store, "config set-default-endpoint https://api.anthropic.com anthropic");
            var saved3 = store.Load();
            await Assert.That(saved3.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
