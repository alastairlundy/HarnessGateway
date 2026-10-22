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
        int previous = ConsoleErrorCapture.Depth.Value;
        bool ownsGate = previous == 0;
        ConsoleErrorCapture.Depth.Value = previous + 1;
        if (ownsGate)
        {
            ConsoleErrorCapture.Gate.Wait();
        }
        try
        {
            return result.Invoke();
        }
        finally
        {
            if (ownsGate)
            {
                ConsoleErrorCapture.Gate.Release();
            }
            ConsoleErrorCapture.Depth.Value = previous;
        }
    }

    private static string Fingerprint(SettingsStore store) =>
        Convert.ToBase64String(File.ReadAllBytes(store.SettingsFilePath));

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
    public async Task Config_surface_is_show_get_set_and_backend()
    {
        ConfigCommand cmd = new ConfigCommand(
            CreateSettingsStore(CreateTempDir()),
            new FakeSecureKeyStore(),
            new ScriptedKeyPrompt(null));

        List<string> verbs = cmd.Subcommands.Select(c => c.Name).ToList();

        await Assert.That(verbs).Contains("show");
        await Assert.That(verbs).Contains("get");
        await Assert.That(verbs).Contains("set");
        await Assert.That(verbs).Contains("backend");
        await Assert.That(verbs.Count).IsEqualTo(4);
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
    public async Task Show_no_backend_hint_points_at_new_verbs()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            // ResolveDefaultBackend falls back to the first entry when the
            // pointer is empty, so the no-default hint only surfaces with a
            // genuinely empty backend list (or a dangling pointer).
            store.Save(new LauncherSettings());

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("No default backend configured.");
            await Assert.That(output).Contains("config backend add");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Show_dangling_pointer_hint_points_at_defaultBackend_write()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "ghost"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("No default backend configured.");
            await Assert.That(output).Contains("config set defaultBackend");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_defaultBackend_reads_the_pointer()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get defaultBackend")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_schemaVersion_reads_the_stamped_version()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings());

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get schemaVersion")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains(LauncherSettings.CurrentSchemaVersion.ToString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_backends_lists_names_only()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default"
                    },
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.Anthropic
                    }
                ],
                DefaultBackend = "default"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("default");
            await Assert.That(output).Contains("work");
            await Assert.That(output.Contains("https://")).IsFalse();
            await Assert.That(output.Contains("hgkey")).IsFalse();
            await Assert.That(output.Contains("anthropic")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_backend_fields_reads_each_typed_shape()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultBackend = "default"
            });

            (int endpointExit, string endpointOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends.default.endpoint")));
            (int protocolExit, string protocolOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends.default.protocol")));
            (int modelExit, string modelOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends.default.defaultModel")));

            await Assert.That(endpointExit).IsEqualTo(0);
            await Assert.That(endpointOutput).Contains("https://api.example.com/v1");
            await Assert.That(protocolExit).IsEqualTo(0);
            await Assert.That(protocolOutput).Contains("openai-chat");
            await Assert.That(modelExit).IsEqualTo(0);
            await Assert.That(modelOutput).Contains("gpt-4");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_unknown_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends.default.apiKey")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("Unknown settings key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_unaddressable_backend_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get backends.nope.endpoint")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("no backend entry named 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultBackend_persists_canonical_name_and_confirms()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    },
                    new Backend
                    {
                        Name = "other",
                        BaseUri = new Uri("https://other.example.com/v1"),
                        Protocol = ProtocolKind.Anthropic
                    }
                ],
                DefaultBackend = string.Empty
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultBackend WORK")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("Default backend set to 'work'.");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.DefaultBackend).IsEqualTo("work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultBackend_already_default_is_idempotent_and_leaves_file_unchanged()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"work","baseUri":"https://api.example.com/v1","protocol":0}],"defaultBackend":"work","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultBackend work")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("Default backend set to 'work'.");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultBackend_unknown_name_returns_exit_8_with_contract_message()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = string.Empty
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultBackend nope")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("unknown backend name 'nope'");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.DefaultBackend).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_schemaVersion_is_rejected_as_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[],"defaultBackend":"","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set schemaVersion 99")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("read-only");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_backends_is_rejected_as_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set backends work")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("read-only");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_backend_endpoint_persists_and_confirms()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://old.example.com/v1"),
                        Protocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:work",
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultBackend = "work"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set backends.work.endpoint https://new.example.com/v1")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("backends.work.endpoint set to https://new.example.com/v1");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
            // A field write never disturbs the sibling fields or the key reference.
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_backend_protocol_persists()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });

            int exitCode = InvokeConfig(store, "config set backends.work.protocol anthropic");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_backend_defaultModel_persists()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });

            int exitCode = InvokeConfig(store, "config set backends.work.defaultModel claude-sonnet-5");

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
    public async Task Set_unknown_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"default","baseUri":"https://api.example.com/v1","protocol":0,"apiKey":"hgkey:default"}],"defaultBackend":"default","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set backends.default.apiKey sk-evil")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("Unknown settings key");
            // The hard boundary: no config set path can write secret material.
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_invalid_protocol_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set backends.work.protocol grpc")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("Invalid protocol 'grpc'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_invalid_endpoint_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set backends.work.endpoint not-a-url")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("Invalid endpoint 'not-a-url'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_unaddressable_backend_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    new Backend
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        Protocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set backends.nope.endpoint https://x.example/v1")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("no backend entry named 'nope'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Removed_config_verbs_no_longer_parse()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            string[] removed =
            [
                "config set-default-endpoint https://api.example.com/v1 openai-chat",
                "config set-default-model gpt-4",
                "config set-saved-key my-key",
                "config clear-saved-key"
            ];

            foreach (string args in removed)
            {
                (int exitCode, _) = await ConsoleErrorCapture.RunAsync(() =>
                    Task.FromResult(InvokeConfig(store, args)));
                await Assert.That(exitCode is not 0).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_on_unsupported_schema_version_returns_exit_9()
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
                Task.FromResult(InvokeConfig(store, "config get defaultBackend")));

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
    public async Task Set_on_unsupported_schema_version_returns_exit_9()
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
                    "config set backends.work.endpoint https://api.example.com/v1")));

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
