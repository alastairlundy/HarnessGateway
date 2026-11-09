using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Providers;
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

    private static int InvokeConfig(SettingsStore store, string args)
    {
        ConfigCommand cmd = new ConfigCommand(store);
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
    public async Task Config_surface_is_show_get_set_only_with_no_backend_subcommand()
    {
        ConfigCommand cmd = new ConfigCommand(CreateSettingsStore(CreateTempDir()));

        List<string> verbs = cmd.Subcommands.Select(c => c.Name).ToList();

        await Assert.That(verbs).Contains("show");
        await Assert.That(verbs).Contains("get");
        await Assert.That(verbs).Contains("set");
        // The provider surface moved to the top level (D004): config carries
        // no backend subcommand and no deprecated alias.
        await Assert.That(verbs).DoesNotContain("backend");
        await Assert.That(verbs.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ResolveDefaultProvider_returns_default_provider_by_name()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "other",
                    BaseUri = new Uri("https://other.example.com/v1"),
                    DefaultProtocol = ProtocolKind.Anthropic
                },
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = "default"
        };

        Provider? backend = ConfigCommand.ResolveDefaultProvider(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("default");
        await Assert.That(backend.BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task ResolveDefaultProvider_returns_first_when_no_default_set()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "first",
                    BaseUri = new Uri("https://first.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = string.Empty
        };

        Provider? backend = ConfigCommand.ResolveDefaultProvider(settings);

        await Assert.That(backend).IsNotNull();
        await Assert.That(backend!.Name).IsEqualTo("first");
    }

    [Test]
    public async Task ResolveDefaultProvider_returns_null_when_no_providers()
    {
        LauncherSettings settings = new LauncherSettings();

        Provider? backend = ConfigCommand.ResolveDefaultProvider(settings);

        await Assert.That(backend).IsNull();
    }

    [Test]
    public async Task UpdateProvider_updates_existing_provider()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://old.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat
                }
            ],
            DefaultProvider = "default"
        };

        Provider updated = new Provider
        {
            Name = "default",
            BaseUri = new Uri("https://new.example.com/v1"),
            DefaultProtocol = ProtocolKind.Anthropic
        };

        ConfigCommand.UpdateProvider(settings, updated);

        await Assert.That(settings.Providers).Count().IsEqualTo(1);
        await Assert.That(settings.Providers[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
        await Assert.That(settings.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
    }

    [Test]
    public async Task UpdateProvider_adds_new_provider_when_not_exists()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Providers = [],
            DefaultProvider = "new"
        };

        Provider newBackend = new Provider
        {
            Name = "new",
            BaseUri = new Uri("https://new.example.com/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat
        };

        ConfigCommand.UpdateProvider(settings, newBackend);

        await Assert.That(settings.Providers).Count().IsEqualTo(1);
        await Assert.That(settings.Providers[0].Name).IsEqualTo("new");
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
                Providers =
                [
                    new Provider
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default",
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultProvider = "default"
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
    public async Task Show_verb_no_provider_returns_zero()
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
    public async Task Show_no_provider_hint_points_at_new_verbs()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            // ResolveDefaultProvider falls back to the first entry when the
            // pointer is empty, so the no-default hint only surfaces with a
            // genuinely empty backend list (or a dangling pointer).
            store.Save(new LauncherSettings());

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("No default provider configured.");
            await Assert.That(output).Contains("provider add");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Show_dangling_pointer_hint_points_at_defaultProvider_write()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "ghost"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("No default provider configured.");
            await Assert.That(output).Contains("config set defaultProvider");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_defaultProvider_reads_the_pointer()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get defaultProvider")));

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
    public async Task Get_providers_lists_names_only()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:default"
                    },
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.Anthropic
                    }
                ],
                DefaultProvider = "default"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get providers")));

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
    public async Task Get_provider_fields_reads_each_typed_shape()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "default",
                        BaseUri = new Uri("https://api.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultProvider = "default"
            });

            (int endpointExit, string endpointOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get providers.default.endpoint")));
            (int protocolExit, string protocolOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get providers.default.protocol")));
            (int modelExit, string modelOutput) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get providers.default.defaultModel")));

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
                Task.FromResult(InvokeConfig(store, "config get providers.default.apiKey")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("Unknown settings key");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Get_unaddressable_provider_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get providers.nope.endpoint")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("no provider entry named 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultProvider_persists_canonical_name_and_confirms()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    },
                    new Provider
                    {
                        Name = "other",
                        BaseUri = new Uri("https://other.example.com/v1"),
                        DefaultProtocol = ProtocolKind.Anthropic
                    }
                ],
                DefaultProvider = string.Empty
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultProvider WORK")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("Default provider set to 'work'.");

            LauncherSettings saved = await store.LoadAsync();
            await Assert.That(saved.DefaultProvider).IsEqualTo("work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultProvider_already_default_is_idempotent_and_leaves_file_unchanged()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"providers":[{"name":"work","baseUri":"https://api.example.com/v1","protocol":0}],"defaultProvider":"work","schemaVersion":3}""");
            string before = Fingerprint(store);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultProvider work")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("Default provider set to 'work'.");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_defaultProvider_unknown_name_returns_exit_8_with_contract_message()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = string.Empty
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set defaultProvider nope")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownProviderName);
            await Assert.That(errorOutput).Contains("unknown provider name 'nope'");

            LauncherSettings saved = await store.LoadAsync();
            await Assert.That(saved.DefaultProvider).IsEqualTo(string.Empty);
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
                """{"providers":[],"defaultProvider":"","schemaVersion":3}""");
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
    public async Task Set_providers_is_rejected_as_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set providers work")));

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
    public async Task Set_provider_endpoint_persists_and_confirms()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://old.example.com/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat,
                        ApiKey = "hgkey:work",
                        DefaultModel = "gpt-4"
                    }
                ],
                DefaultProvider = "work"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set providers.work.endpoint https://new.example.com/v1")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("providers.work.endpoint set to https://new.example.com/v1");

            LauncherSettings saved = await store.LoadAsync();
            await Assert.That(saved.Providers[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
            // A field write never disturbs the sibling fields or the key reference.
            await Assert.That(saved.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
            await Assert.That(saved.Providers[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(saved.Providers[0].ApiKey).IsEqualTo("hgkey:work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_provider_protocol_persists()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });

            int exitCode = InvokeConfig(store, "config set providers.work.protocol anthropic");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = await store.LoadAsync();
            await Assert.That(saved.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Set_provider_defaultModel_persists()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });

            int exitCode = InvokeConfig(store, "config set providers.work.defaultModel claude-sonnet-5");

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = await store.LoadAsync();
            await Assert.That(saved.Providers[0].DefaultModel).IsEqualTo("claude-sonnet-5");
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
                """{"providers":[{"name":"default","baseUri":"https://api.example.com/v1","protocol":0,"apiKey":"hgkey:default"}],"defaultProvider":"default","schemaVersion":3}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set providers.default.apiKey sk-evil")));

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
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set providers.work.protocol grpc")));

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
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config set providers.work.endpoint not-a-url")));

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
    public async Task Set_unaddressable_provider_key_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Providers =
                [
                    new Provider
                    {
                        Name = "work",
                        BaseUri = new Uri("https://api.work.example/v1"),
                        DefaultProtocol = ProtocolKind.OpenAiChat
                    }
                ],
                DefaultProvider = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set providers.nope.endpoint https://x.example/v1")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("no provider entry named 'nope'");
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
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 4 }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config get defaultProvider")));

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
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 4 }""");
            string before = File.ReadAllText(store.SettingsFilePath);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config set providers.work.endpoint https://api.example.com/v1")));

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
                """{ "backends": [], "defaultBackend": "", "schemaVersion": 4 }""");
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
    public async Task Load_migration_write_failure_returns_config_restore_failure_not_a_crash()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"work","baseUri":"https://api.example.com/v1","protocol":0}],"defaultBackend":"work","schemaVersion":2}""");

            // Fail the migration rewrite (see SettingsStoreTests): the load
            // fault must surface as the documented ConfigRestoreFailure, not
            // as an unhandled exception with a stack trace and an exit code
            // outside the contract.
            Directory.CreateDirectory(store.SettingsFilePath + ".migrating");

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config show")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(string.IsNullOrWhiteSpace(errorOutput)).IsFalse();
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

            int exitCode = InvokeConfig(store, "config show");
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
