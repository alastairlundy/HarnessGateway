using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

public class BackendCommandsTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-backend-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsStore CreateSettingsStore(string tempDir, FakeSecureKeyStore? keyStore = null)
    {
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        return new SettingsStore(settingsDir, keyStore);
    }

    private static int InvokeConfig(
        SettingsStore store,
        string args,
        FakeSecureKeyStore? keyStore = null,
        IKeyPrompt? keyPrompt = null)
    {
        ConfigCommand cmd = new ConfigCommand(store, keyStore ?? new FakeSecureKeyStore(), keyPrompt ?? new ScriptedKeyPrompt(null));
        RootCommand rootCmd = new RootCommand { cmd };
        ParseResult result = rootCmd.Parse(args);
        return result.Invoke();
    }

    private static Backend CreateBackend(
        string name,
        string url = "https://api.example.com/v1",
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? apiKey = null,
        string? defaultModel = null)
    {
        return new Backend
        {
            Name = name,
            BaseUri = new Uri(url),
            Protocol = protocol,
            ApiKey = apiKey,
            DefaultModel = defaultModel
        };
    }

    private static string Fingerprint(SettingsStore store) =>
        Convert.ToBase64String(File.ReadAllBytes(store.SettingsFilePath));

    [Test]
    public async Task Backend_command_exposes_five_verbs_with_no_aliases()
    {
        string tempDir = CreateTempDir();
        try
        {
            ConfigCommand cmd = new ConfigCommand(
                CreateSettingsStore(tempDir),
                new FakeSecureKeyStore(),
                new ScriptedKeyPrompt(null));

            Command backend = cmd.Subcommands.Single(s => s.Name == "backend");
            List<string> verbs = backend.Subcommands.Select(c => c.Name).ToList();

            await Assert.That(verbs.Count).IsEqualTo(5);
            await Assert.That(verbs).Contains("add");
            await Assert.That(verbs).Contains("list");
            await Assert.That(verbs).Contains("remove");
            await Assert.That(verbs).Contains("set-key");
            await Assert.That(verbs).Contains("clear-key");
            await Assert.That(verbs).DoesNotContain("switch");

            foreach (Command sub in backend.Subcommands)
            {
                await Assert.That(sub.Aliases).IsEmpty();
                foreach (Option option in sub.Options)
                {
                    await Assert.That(option.Aliases).IsEmpty();
                }
            }

            Command addVerb = backend.Subcommands.Single(c => c.Name == "add");
            await Assert.That(addVerb.Options.Count).IsEqualTo(3);

            Command setKeyVerb = backend.Subcommands.Single(c => c.Name == "set-key");
            await Assert.That(setKeyVerb.Options.Count).IsEqualTo(1);

            Command clearKeyVerb = backend.Subcommands.Single(c => c.Name == "clear-key");
            await Assert.That(clearKeyVerb.Options).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Removed_backend_switch_verb_no_longer_parses()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = string.Empty
            });

            (int exitCode, _) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend switch work")));

            await Assert.That(exitCode is not 0).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_creates_keyless_entry_and_leaves_pointer_empty()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt(null);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend add work --endpoint https://api.work.example/v1 --protocol openai-chat",
                    keyStore,
                    prompt)));

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends.Count).IsEqualTo(1);
            await Assert.That(saved.Backends[0].Name).IsEqualTo("work");
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://api.work.example/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);

            // add never moves the default pointer (config set defaultBackend
            // owns it), and a declined prompt yields a keyless entry with no
            // store call. The create hint names the pointer write path.
            await Assert.That(saved.DefaultBackend).IsEqualTo(string.Empty);
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);
            await Assert.That(prompt.CallCount).IsEqualTo(1);
            await Assert.That(output).Contains("config set defaultBackend work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_replaces_entry_preserving_model_and_key_reference()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            keyStore.Seed("hgkey:work", "old-secret");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend(
                        "work",
                        url: "https://old.example.com/v1",
                        protocol: ProtocolKind.OpenAiChat,
                        apiKey: "hgkey:work",
                        defaultModel: "gpt-4")
                ],
                DefaultBackend = "work"
            });

            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt(null);
            int exitCode = InvokeConfig(
                store,
                "config backend add work --endpoint https://new.example.com/v1 --protocol anthropic",
                keyStore,
                prompt);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends.Count).IsEqualTo(1);
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(saved.DefaultBackend).IsEqualTo("work");
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);
            await Assert.That(prompt.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_key_flag_stores_secret_and_persists_only_a_reference()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);

            int exitCode = InvokeConfig(
                store,
                "config backend add work --endpoint https://api.work.example/v1 --protocol openai-chat --key my-secret-key",
                keyStore);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(keyStore.Contains("hgkey:work")).IsTrue();
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(1);

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            await Assert.That(fileBytes.Contains("hgkey:")).IsTrue();
            await Assert.That(fileBytes.Contains("my-secret-key")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_prompt_not_invoked_when_key_flag_given()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt("ignored-because-flag-wins");

            int exitCode = InvokeConfig(
                store,
                "config backend add work --endpoint https://api.work.example/v1 --protocol openai-chat --key flagged-secret",
                null,
                prompt);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(prompt.CallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_prompt_flow_stores_secret_and_persists_only_a_reference()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt("prompted-secret");

            int exitCode = InvokeConfig(
                store,
                "config backend add work --endpoint https://api.work.example/v1 --protocol openai-chat",
                keyStore,
                prompt);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(keyStore.Contains("hgkey:work")).IsTrue();
            await Assert.That(prompt.CallCount).IsEqualTo(1);

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            await Assert.That(fileBytes.Contains("hgkey:")).IsTrue();
            await Assert.That(fileBytes.Contains("prompted-secret")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_missing_both_flags_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend add work")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("config backend add requires --endpoint and --protocol");
            await Assert.That(File.Exists(store.SettingsFilePath)).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_one_sided_pair_returns_resolver_messages()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            (int endpointOnlyExit, string endpointOnlyError) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend add work --endpoint https://api.example.com/v1")));

            await Assert.That(endpointOnlyExit).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(endpointOnlyError).Contains("--endpoint requires --protocol to be specified");

            (int protocolOnlyExit, string protocolOnlyError) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend add work --protocol openai-chat")));

            await Assert.That(protocolOnlyExit).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(protocolOnlyError).Contains("--protocol requires --endpoint to be specified");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_invalid_endpoint_or_protocol_returns_usage_error()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            (int badUrlExit, string badUrlError) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend add work --endpoint not-a-url --protocol openai-chat")));

            await Assert.That(badUrlExit).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(badUrlError).Contains("Invalid endpoint 'not-a-url'");

            (int badProtocolExit, string badProtocolError) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend add work --endpoint https://api.example.com/v1 --protocol grpc")));

            await Assert.That(badProtocolExit).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(badProtocolError).Contains("Invalid protocol 'grpc'");
            await Assert.That(File.Exists(store.SettingsFilePath)).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Add_store_failure_returns_exit_7_without_writing_settings()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore { FailStore = true };
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"default","baseUri":"https://api.example.com/v1","protocol":0}],"defaultBackend":"default","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend add new --endpoint https://api.new.example/v1 --protocol openai-chat --key sk-new",
                    keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(errorOutput).Contains("backend 'new'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_with_flag_stores_reference_only_and_leaves_entry_fields_untouched()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend(
                        "work",
                        url: "https://api.work.example/v1",
                        protocol: ProtocolKind.Anthropic,
                        defaultModel: "gpt-4")
                ],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend set-key work --key my-secret-key",
                    keyStore)));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("API key for backend 'work' saved.");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(keyStore.Contains("hgkey:work")).IsTrue();
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(1);

            // set-key touches only the credential record.
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://api.work.example/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(saved.DefaultBackend).IsEqualTo("work");

            string fileBytes = File.ReadAllText(store.SettingsFilePath);
            await Assert.That(fileBytes.Contains("hgkey:work")).IsTrue();
            await Assert.That(fileBytes.Contains("my-secret-key")).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_prompt_flow_stores_reference_only()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = "work"
            });
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt("prompted-secret");

            int exitCode = InvokeConfig(
                store,
                "config backend set-key work",
                keyStore,
                prompt);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
            await Assert.That(keyStore.Contains("hgkey:work")).IsTrue();
            await Assert.That(prompt.CallCount).IsEqualTo(1);
            // Prompt path receives entryHasKey=false for a keyless backend.
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_prompt_not_invoked_when_key_flag_given()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work", apiKey: "hgkey:work")],
                DefaultBackend = "work"
            });
            keyStore.Seed("hgkey:work", "old-secret");
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt("ignored-because-flag-wins");

            int exitCode = InvokeConfig(
                store,
                "config backend set-key work --key flagged-secret",
                keyStore,
                prompt);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(prompt.CallCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_declined_prompt_leaves_entry_unchanged()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);
            ScriptedKeyPrompt prompt = new ScriptedKeyPrompt(null);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend set-key work", keyStore, prompt)));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("left unchanged");
            await Assert.That(prompt.CallCount).IsEqualTo(1);
            await Assert.That(keyStore.StoreCallCount).IsEqualTo(0);
            await Assert.That(Fingerprint(store)).IsEqualTo(before);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_unknown_name_returns_exit_8()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = "work"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend set-key nope --key sk-x")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("unknown backend name 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task SetKey_store_failure_returns_exit_7_without_writing_settings()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore { FailStore = true };
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"work","baseUri":"https://api.work.example/v1","protocol":2,"defaultModel":"gpt-4"}],"defaultBackend":"work","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(
                    store,
                    "config backend set-key work --key sk-new",
                    keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(errorOutput).Contains("backend 'work'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearKey_removes_store_entry_and_nulls_reference_leaving_fields_untouched()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            keyStore.Seed("hgkey:work", "old-key");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend(
                        "work",
                        url: "https://api.work.example/v1",
                        protocol: ProtocolKind.Anthropic,
                        apiKey: "hgkey:work",
                        defaultModel: "gpt-4")
                ],
                DefaultBackend = "work"
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend clear-key work", keyStore)));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("API key for backend 'work' cleared.");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsNull();
            await Assert.That(keyStore.Contains("hgkey:work")).IsFalse();
            await Assert.That(keyStore.DeleteCallCount).IsEqualTo(1);

            // clear-key never alters endpoint, protocol, or default model.
            await Assert.That(saved.Backends[0].BaseUri).IsEqualTo(new Uri("https://api.work.example/v1"));
            await Assert.That(saved.Backends[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
            await Assert.That(saved.Backends[0].DefaultModel).IsEqualTo("gpt-4");
            await Assert.That(saved.DefaultBackend).IsEqualTo("work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearKey_keyless_backend_is_idempotent()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = "work"
            });
            string before = Fingerprint(store);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend clear-key work", keyStore)));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("nothing to clear");
            await Assert.That(keyStore.DeleteCallCount).IsEqualTo(0);
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearKey_unknown_name_returns_exit_8()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends = [CreateBackend("work")],
                DefaultBackend = "work"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend clear-key nope")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("unknown backend name 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearKey_store_delete_failure_returns_exit_7_and_keeps_entry()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore { FailDelete = true };
            keyStore.Seed("hgkey:work", "old-key");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"work","baseUri":"https://api.work.example/v1","protocol":0,"apiKey":"hgkey:work"}],"defaultBackend":"work","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend clear-key work", keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(errorOutput).Contains("backend 'work'");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
            await Assert.That(keyStore.Contains("hgkey:work")).IsTrue();

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends[0].ApiKey).IsEqualTo("hgkey:work");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Remove_deletes_entry_and_store_secret()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            keyStore.Seed("hgkey:beta", "beta-secret");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend("alpha", url: "https://alpha.example.com/v1"),
                    CreateBackend("beta", url: "https://beta.example.com/v1", apiKey: "hgkey:beta")
                ],
                DefaultBackend = "alpha"
            });

            int exitCode = InvokeConfig(store, "config backend remove beta", keyStore);

            await Assert.That(exitCode).IsEqualTo(0);

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends.Count).IsEqualTo(1);
            await Assert.That(saved.Backends[0].Name).IsEqualTo("alpha");
            await Assert.That(keyStore.Contains("hgkey:beta")).IsFalse();
            await Assert.That(keyStore.DeleteCallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Remove_last_entry_is_refused()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"work","baseUri":"https://api.example.com/v1","protocol":0}],"defaultBackend":"work","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend remove work")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);

            // The last-entry guard wins even though the entry is also the default.
            await Assert.That(errorOutput).Contains("it is the last saved backend");
            await Assert.That(errorOutput.Contains("current default backend")).IsFalse();
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Remove_current_default_is_refused()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore();
            keyStore.Seed("hgkey:alpha", "alpha-secret");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            File.WriteAllText(
                store.SettingsFilePath,
                """{"backends":[{"name":"alpha","baseUri":"https://alpha.example.com/v1","protocol":0,"apiKey":"hgkey:alpha"},{"name":"beta","baseUri":"https://beta.example.com/v1","protocol":0}],"defaultBackend":"alpha","schemaVersion":2}""");
            string before = Fingerprint(store);

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend remove alpha", keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("it is the current default backend");
            await Assert.That(errorOutput).Contains("config set defaultBackend");
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
            await Assert.That(keyStore.DeleteCallCount).IsEqualTo(0);
            await Assert.That(keyStore.Contains("hgkey:alpha")).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Remove_unknown_name_returns_exit_8()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend("alpha", url: "https://alpha.example.com/v1"),
                    CreateBackend("beta", url: "https://beta.example.com/v1")
                ],
                DefaultBackend = "alpha"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend remove nope")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("unknown backend name 'nope'");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Remove_store_delete_failure_returns_exit_7_and_keeps_entry()
    {
        string tempDir = CreateTempDir();
        try
        {
            FakeSecureKeyStore keyStore = new FakeSecureKeyStore { FailDelete = true };
            keyStore.Seed("hgkey:beta", "beta-secret");
            SettingsStore store = CreateSettingsStore(tempDir, keyStore);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend("alpha", url: "https://alpha.example.com/v1"),
                    CreateBackend("beta", url: "https://beta.example.com/v1", apiKey: "hgkey:beta")
                ],
                DefaultBackend = "alpha"
            });

            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend remove beta", keyStore)));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.ConfigRestoreFailure);
            await Assert.That(errorOutput).Contains("backend 'beta'");

            LauncherSettings saved = store.Load();
            await Assert.That(saved.Backends.Count).IsEqualTo(2);
            await Assert.That(keyStore.Contains("hgkey:beta")).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task List_is_read_only_and_marks_only_the_default()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend("alpha", url: "https://alpha.example.com/v1"),
                    CreateBackend("beta", url: "https://beta.example.com/v1", protocol: ProtocolKind.Anthropic)
                ],
                DefaultBackend = "beta"
            });
            string before = Fingerprint(store);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend list")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(Fingerprint(store)).IsEqualTo(before);
            await Assert.That(output).Contains("* beta  https://beta.example.com/v1  anthropic");
            await Assert.That(output).Contains("  alpha  https://alpha.example.com/v1  openai-chat");
            await Assert.That(output.Contains("* alpha")).IsFalse();
            await Assert.That(output).Contains("(* = default backend)");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task List_without_default_notes_pointer_write_in_footer()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);
            store.Save(new LauncherSettings
            {
                Backends =
                [
                    CreateBackend("alpha", url: "https://alpha.example.com/v1"),
                    CreateBackend("beta", url: "https://beta.example.com/v1")
                ],
                DefaultBackend = string.Empty
            });

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend list")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output.Contains("* ")).IsFalse();
            await Assert.That(output).Contains("no default backend is set");
            await Assert.That(output).Contains("config set defaultBackend");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task List_empty_returns_zero_with_add_guidance()
    {
        string tempDir = CreateTempDir();
        try
        {
            SettingsStore store = CreateSettingsStore(tempDir);

            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(() =>
                Task.FromResult(InvokeConfig(store, "config backend list")));

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output).Contains("config backend add");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Backend_verbs_refuse_unsupported_schema_version()
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
                Task.FromResult(InvokeConfig(store, "config backend list")));

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnsupportedSettingsSchema);
            await Assert.That(errorOutput).Contains("unsupported settings schema version");
            await Assert.That(File.ReadAllText(store.SettingsFilePath)).IsEqualTo(before);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
