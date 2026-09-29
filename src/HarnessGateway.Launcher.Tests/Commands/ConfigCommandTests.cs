using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

public class ConfigCommandTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsStore CreateSettingsStore(string tempDir, FakeSecureKeyStore? keyStore = null)
    {
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        return new SettingsStore(settingsDir, keyStore);
    }

    private static int InvokeConfig(SettingsStore store, string args, FakeSecureKeyStore? keyStore = null)
    {
        ConfigCommand cmd = new ConfigCommand(store, keyStore ?? new FakeSecureKeyStore());
        RootCommand rootCmd = new RootCommand { cmd };
        ParseResult result = rootCmd.Parse(args);
        return result.Invoke();
    }

    [Test]
    public async Task Config_verbs_define_no_short_aliases()
    {
        ConfigCommand cmd = new ConfigCommand(CreateSettingsStore(CreateTempDir()));

        List<Command> subcommands = cmd.Subcommands.ToList();
        await Assert.That(subcommands).IsNotEmpty();

        foreach (Command sub in subcommands)
        {
            await Assert.That(sub.Aliases).IsEmpty();
            foreach (Option option in sub.Options)
            {
                await Assert.That(option.Aliases).IsEmpty();
            }
        }

        await Assert.That(cmd.Options).IsEmpty();
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_default_backend_by_name()
    {
        LauncherSettings settings = new LauncherSettings
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

        Backend? backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("default");
        await Assert.That(backend.BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_first_when_no_default_set()
    {
        LauncherSettings settings = new LauncherSettings
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

        Backend? backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("first");
    }

    [Test]
    public async Task ResolveDefaultBackend_returns_null_when_no_backends()
    {
        LauncherSettings settings = new LauncherSettings();

        Backend? backend = ConfigCommand.ResolveDefaultBackend(settings);

        await Assert.That(backend).IsNull();
    }

    [Test]
    public async Task UpdateBackend_updates_existing_backend()
    {
        LauncherSettings settings = new LauncherSettings
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

        Backend updated = new Backend
        {
            Name = "default",
            BaseUri = new Uri("https://new.example.com/v1"),
            Protocol = ProtocolKind.Anthropic
        };

        ConfigCommand.UpdateBackend(settings, updated);

        await Assert.That(settings.Backends).Count().IsEqualTo(1);
        await Assert.That(settings.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
        await Assert.That(settings.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
    }

    [Test]
    public async Task UpdateBackend_adds_new_backend_when_not_exists()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends = [],
            DefaultBackend = "new"
        };

        Backend newBackend = new Backend
        {
            Name = "new",
            BaseUri = new Uri("https://new.example.com/v1"),
            Protocol = ProtocolKind.OpenAiChat
        };

        ConfigCommand.UpdateBackend(settings, newBackend);

        await Assert.That(settings.Backends).Count().IsEqualTo(1);
        await Assert.That(settings.Backends[0].Name).IsEqualTo("new");
    }

    [Test]
    public async Task Show_verb_returns_zero()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default",
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            int exitCode = InvokeConfig(store, "config show");

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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config show");

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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-chat");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.DefaultBackend).IsEqualTo("default");
            await Assert.That(saved.Backends).Count().IsEqualTo(1);
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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config set-default-endpoint https://api.example.com/v1 invalid-protocol");

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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings existing = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://old.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default",
                        DefaultModel = "existing-model"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(existing);

            int exitCode = InvokeConfig(store, "config set-default-endpoint https://new.example.com/v1 anthropic");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:default");
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("existing-model");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetSavedKey_stores_the_secret_and_persists_only_a_reference()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
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

            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            int exitCode = InvokeConfig(store, "config set-saved-key my-secret-key", keyStore);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:default");

            // The secret lives only in the store, never in the settings file.
            await Assert.That(keyStore.Contains("hgkey:default")).IsTrue();
            string fileBytes = await File.ReadAllTextAsync(store.SettingsFilePath);
            await Assert.That(fileBytes.Contains("hgkey:")).IsTrue();
            await Assert.That(fileBytes.Contains("my-secret-key")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetSavedKey_store_failure_returns_exit_7()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
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

            FakeSecureKeyStore keyStore = new FakeSecureKeyStore { FailStore = true };

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set-saved-key my-secret-key", keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(errorOutput).Contains("config set-saved-key");

            // Nothing was persisted: the backend still has no key at all.
            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetSavedKey_no_backend_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config set-saved-key my-key");

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearSavedKey_deletes_the_store_entry_and_nulls_the_reference()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default"
                    }
                ],
                DefaultBackend = "default"
            };
            store.Save(settings);

            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            keyStore.Seed("hgkey:default", "old-key");

            int exitCode = InvokeConfig(store, "config clear-saved-key", keyStore);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
            await Assert.That(keyStore.Contains("hgkey:default")).IsFalse();
            await Assert.That(keyStore.DeleteCallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearSavedKey_no_backend_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config clear-saved-key");

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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            LauncherSettings settings = new LauncherSettings
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

            int exitCode = InvokeConfig(store, "config set-default-model claude-sonnet-5");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            int exitCode = InvokeConfig(store, "config set-default-model gpt-4");

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
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-chat");
            LauncherSettings saved1 = store.Load();
            await Assert.That(saved1.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);

            InvokeConfig(store, "config set-default-endpoint https://api.openai.com/v1 openai-responses");
            LauncherSettings saved2 = store.Load();
            await Assert.That(saved2.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);

            InvokeConfig(store, "config set-default-endpoint https://api.anthropic.com anthropic");
            LauncherSettings saved3 = store.Load();
            await Assert.That(saved3.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Show_on_unsupported_schema_version_returns_exit_9()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnsupportedSettingsSchema);
            await Assert.That(errorOutput).Contains("unsupported settings schema version");
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetDefaultEndpoint_on_unsupported_schema_version_returns_exit_9()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 3 }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set-default-endpoint https://api.example.com/v1 openai-chat")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnsupportedSettingsSchema);
            await Assert.That(errorOutput).Contains("unsupported settings schema version");
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Show_migrates_a_v1_plaintext_file_before_reading_it()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            File.WriteAllText(
                store.SettingsFilePath,
                """
                {
                  "backends": [
                    {
                      "name": "default",
                      "baseUri": "https://api.example.com/v1",
                      "protocol": 0,
                      "apiKey": "sk-legacy-plaintext"
                    }
                  ],
                  "defaultBackend": "default",
                  "schemaVersion": 1
                }
                """);

            int exitCode = InvokeConfig(store, "config show", keyStore);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(1);
            await Assert.That(keyStore.Contains("hgkey:default")).IsTrue();

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            string expectedStamp = $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}";
            await Assert.That(fileBytes.Contains(expectedStamp)).IsTrue();
            await Assert.That(fileBytes.Contains("hgkey:default")).IsTrue();
            await Assert.That(fileBytes.Contains("sk-legacy-plaintext")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
