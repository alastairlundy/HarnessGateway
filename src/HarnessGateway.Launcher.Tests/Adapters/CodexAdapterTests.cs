using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Toml;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class CodexAdapterTests
{
    private static readonly string UserDotCodexConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "config.toml");

    private static readonly string OriginalConfig = ("""
        # ~/.codex/config.toml
        model = "gpt-5-codex"
        model_provider = "openai"

        # Keep the workspace writable for edits
        sandbox_mode = "workspace-write"

        [model_providers.openai]
        name = "OpenAI"
        base_url = "https://api.openai.com/v1"
        env_key = "OPENAI_API_KEY"
        wire_api = "chat"

        [features]
        multi_agent = true
        """ + "\n").ReplaceLineEndings("\n");

    private static CodexAdapter CreateAdapter(string? codexHome, bool available = true)
    {
        return new CodexAdapter(
            (_, _) => Task.FromResult<string?>(available ? "codex" : null),
            name => name == "CODEX_HOME" ? codexHome : null);
    }

    private static LaunchRequest CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiResponses,
        string? model = "gpt-5",
        string? apiKey = "sk-test",
        string baseUri = "https://api.example.com/v1")
    {
        return new LaunchRequest(
            new Backend
            {
                Name = "test",
                BaseUri = new Uri(baseUri),
                Protocol = protocol,
                ApiKey = apiKey
            },
            Model: model,
            Path.GetTempPath());
    }

    private static (string TempDir, string CodexHome, string ConfigPath) CreateCodexHome()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        var codexHome = Path.Combine(tempDir, "codex-home");
        Directory.CreateDirectory(codexHome);
        return (tempDir, codexHome, Path.Combine(codexHome, "config.toml"));
    }

    [Test]
    public async Task Metadata_declares_name_kind_and_model_requirement()
    {
        var adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.Name).IsEqualTo("codex");
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_install_hint_covers_npm_and_brew()
    {
        var adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.InstallHint).Contains("npm");
        await Assert.That(adapter.Metadata.InstallHint).Contains("brew");
    }

    [Test]
    public async Task Metadata_detection_command_probes_codex()
    {
        var adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.DetectionCommand).IsEqualTo("codex --version");
    }

    [Test]
    public async Task Metadata_supports_only_openai_responses()
    {
        var adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.SupportedProtocols)
            .IsEquivalentTo(new[] { ProtocolKind.OpenAiResponses });
    }

    [Test]
    public async Task Metadata_config_candidate_falls_back_to_user_dot_codex()
    {
        var adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.ConfigFileCandidates)
            .IsEquivalentTo(new[] { UserDotCodexConfig });
    }

    [Test]
    public async Task Metadata_config_candidate_honours_codex_home()
    {
        var (tempDir, codexHome, configPath) = CreateCodexHome();
        try
        {
            var adapter = CreateAdapter(codexHome);

            await Assert.That(adapter.Metadata.ConfigFileCandidates)
                .IsEquivalentTo(new[] { configPath });
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Default_config_path_honours_real_CODEX_home_environment_variable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", tempDir);

            var adapter = new CodexAdapter();

            await Assert.That(adapter.Metadata.ConfigFileCandidates)
                .IsEquivalentTo(new[] { Path.Combine(tempDir, "config.toml") });
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        var adapter = CreateAdapter(codexHome: null);
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        var adapter = CreateAdapter(codexHome: null, available: false);
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_writes_codex_home_config_and_injects_saved_key()
    {
        var (tempDir, codexHome, configPath) = CreateCodexHome();
        try
        {
            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest(apiKey: "sk-live");

            var plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(plan.ConfigurationActions.Count).IsEqualTo(2);

            var fileWrite = plan.ConfigurationActions[0];
            await Assert.That(fileWrite).IsTypeOf<ConfigFileWriteAction>();
            await Assert.That(((ConfigFileWriteAction)fileWrite).TargetPath).IsEqualTo(configPath);

            var envAction = plan.ConfigurationActions[1];
            await Assert.That(envAction).IsTypeOf<EnvironmentVariableAction>();
            await Assert.That(((EnvironmentVariableAction)envAction).Name)
                .IsEqualTo(CodexAdapter.EnvKeyName);
            await Assert.That(((EnvironmentVariableAction)envAction).Value).IsEqualTo("sk-live");

            await Assert.That(plan.Process.TargetFilePath).IsEqualTo("codex");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_writes_provider_block_and_top_level_keys()
    {
        var (tempDir, codexHome, configPath) = CreateCodexHome();
        try
        {
            await File.WriteAllTextAsync(configPath, OriginalConfig);
            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest(model: "gpt-5", baseUri: "https://api.example.com/v1/");

            var plan = await adapter.PlanLaunchAsync(request);
            var content = ((ConfigFileWriteAction)plan.ConfigurationActions[0]).Content;

            // Exactly the added block plus the two top-level keys - everything else
            // (comments, whitespace, unrelated sections) round-trips byte-for-byte.
            var expected = OriginalConfig
                .Replace("model = \"gpt-5-codex\"", "model = \"gpt-5\"", StringComparison.Ordinal)
                .Replace("model_provider = \"openai\"", "model_provider = \"harnessgateway\"", StringComparison.Ordinal)
                + "\n[model_providers.harnessgateway]\n"
                + "name = \"HarnessGateway\"\n"
                + "base_url = \"https://api.example.com/v1\"\n"
                + "env_key = \"HARNESSGATEWAY_API_KEY\"\n"
                + "wire_api = \"responses\"\n";

            await Assert.That(content).IsEqualTo(expected);
            // The editor output must re-parse to itself (round-trip sanity).
            await Assert.That(TomlDocumentEditor.Parse(content).ToToml()).IsEqualTo(content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_creates_config_from_scratch_when_file_missing()
    {
        var (tempDir, codexHome, configPath) = CreateCodexHome();
        try
        {
            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest(model: "gpt-5");

            var plan = await adapter.PlanLaunchAsync(request);
            var write = (ConfigFileWriteAction)plan.ConfigurationActions[0];

            await Assert.That(File.Exists(configPath)).IsFalse();
            await Assert.That(write.TargetPath).IsEqualTo(configPath);
            await Assert.That(write.Content).IsEqualTo(
                "model_provider = \"harnessgateway\"\n"
                + "model = \"gpt-5\"\n"
                + "[model_providers.harnessgateway]\n"
                + "name = \"HarnessGateway\"\n"
                + "base_url = \"https://api.example.com/v1\"\n"
                + "env_key = \"HARNESSGATEWAY_API_KEY\"\n"
                + "wire_api = \"responses\"\n");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task PlanLaunch_omits_key_action_when_no_saved_key(string? apiKey)
    {
        var (tempDir, codexHome, _) = CreateCodexHome();
        try
        {
            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest(apiKey: apiKey);

            var plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(plan.ConfigurationActions.Count).IsEqualTo(1);
            await Assert.That(plan.ConfigurationActions.OfType<EnvironmentVariableAction>()).IsEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_never_writes_a_dummy_key_value()
    {
        var (tempDir, codexHome, _) = CreateCodexHome();
        try
        {
            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest(apiKey: null);

            var plan = await adapter.PlanLaunchAsync(request);
            var content = ((ConfigFileWriteAction)plan.ConfigurationActions[0]).Content;

            await Assert.That(content.Contains("sk-test", StringComparison.Ordinal)).IsFalse();
            await Assert.That(content.Contains("dummy", StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_refuses_when_provider_block_already_exists()
    {
        var (tempDir, codexHome, configPath) = CreateCodexHome();
        try
        {
            const string userOwned = """
                # my own codex config
                [model_providers.harnessgateway]
                name = "Mine"
                base_url = "https://mine.example/v1"
                env_key = "MINE_KEY"
                wire_api = "responses"

                """ + "\n";
            await File.WriteAllTextAsync(configPath, userOwned.ReplaceLineEndings("\n"));

            var adapter = CreateAdapter(codexHome);
            var request = CreateRequest();

            InvalidOperationException? caught = null;
            try
            {
                await adapter.PlanLaunchAsync(request);
            }
            catch (InvalidOperationException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("model_providers.harnessgateway");
            // Refusal happens before any write: the user's file is untouched.
            var after = await File.ReadAllTextAsync(configPath);
            await Assert.That(after).IsEqualTo(userOwned.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
