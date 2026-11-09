using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Toml;
using HarnessGateway.Launcher.Tests.Fakes;

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
            (_, _) => Task.FromResult(available),
            name => name == "CODEX_HOME" ? codexHome : null);
    }

    private static ResolvedLaunch CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiResponses,
        string? model = "gpt-5",
        string? apiKey = "sk-test",
        string baseUri = "https://api.example.com/v1",
        bool adHoc = false,
        IProtocolEmissionPlanner? planner = null)
    {
        Uri baseAddress = new(baseUri);
        Provider provider = new()
        {
            Name = "test",
            BaseUri = baseAddress,
            DefaultProtocol = protocol,
            ApiKey = apiKey
        };

        // The provider case carries the stored entry; the ad-hoc case is the
        // keyless raw-URL arm of the family. The launch-scoped key rides the
        // payload either way - a wizard session key can credential an
        // ad-hoc launch.
        LaunchResolution launch = adHoc
            ? new LaunchResolution.AdHocCase(baseAddress, protocol, model)
            : new LaunchResolution.ProviderCase(provider, model, protocol);

        return new ResolvedLaunch(
            launch,
            apiKey,
            Path.GetTempPath(),
            planner ?? new ProtocolEmissionPlanner());
    }

    private static (string TempDir, string CodexHome, string ConfigPath) CreateCodexHome()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        string codexHome = Path.Combine(tempDir, "codex-home");
        Directory.CreateDirectory(codexHome);
        return (tempDir, codexHome, Path.Combine(codexHome, "config.toml"));
    }

    [Test]
    public async Task Metadata_declares_name_kind_and_model_requirement()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.Name).IsEqualTo("codex");
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_install_hint_covers_npm_and_brew()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.InstallHint).Contains("npm");
        await Assert.That(adapter.Metadata.InstallHint).Contains("brew");
    }

    [Test]
    public async Task Metadata_detection_command_probes_codex()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.DetectionCommand).IsEqualTo("codex --version");
    }

    [Test]
    public async Task Metadata_supports_only_openai_responses()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.SupportedProtocols)
            .IsEquivalentTo([ProtocolKind.OpenAiResponses]);
    }

    [Test]
    public async Task Metadata_config_candidate_falls_back_to_user_dot_codex()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null);

        await Assert.That(adapter.Metadata.ConfigFileCandidates)
            .IsEquivalentTo([UserDotCodexConfig]);
    }

    [Test]
    public async Task Metadata_config_candidate_honours_codex_home()
    {
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);

            await Assert.That(adapter.Metadata.ConfigFileCandidates)
                .IsEquivalentTo([configPath]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    // Shares the constraint key with the other tests that mutate the real process
    // environment, so they never run concurrently.
    [NotInParallel("process-environment")]
    public async Task Default_config_path_honours_real_CODEX_home_environment_variable()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string? previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", tempDir);

            CodexAdapter adapter = new CodexAdapter();

            await Assert.That(adapter.Metadata.ConfigFileCandidates)
                .IsEquivalentTo([Path.Combine(tempDir, "config.toml")]);
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
        CodexAdapter adapter = CreateAdapter(codexHome: null);
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        CodexAdapter adapter = CreateAdapter(codexHome: null, available: false);
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_writes_codex_home_config_and_injects_saved_key()
    {
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(apiKey: "sk-live");

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(plan.ConfigurationActions.Count).IsEqualTo(1);

            ConfigurationAction fileWrite = plan.ConfigurationActions[0];
            await Assert.That(fileWrite).IsTypeOf<ConfigFileWriteAction>();
            await Assert.That(((ConfigFileWriteAction)fileWrite).TargetPath).IsEqualTo(configPath);

            // The saved key is scoped to the spawned codex process via the plan's
            // process configuration, under the env_key variable's name.
            await Assert.That(plan.Process.EnvironmentVariables[CodexAdapter.EnvKeyName])
                .IsEqualTo("sk-live");

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
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            await File.WriteAllTextAsync(configPath, OriginalConfig);
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(model: "gpt-5", baseUri: "https://api.example.com/v1/");

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);
            string content = ((ConfigFileWriteAction)plan.ConfigurationActions[0]).Content;

            // Exactly the added block plus the two top-level keys - everything else
            // (comments, whitespace, unrelated sections) round-trips byte-for-byte.
            string expected = OriginalConfig
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
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(model: "gpt-5");

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);
            ConfigFileWriteAction write = (ConfigFileWriteAction)plan.ConfigurationActions[0];

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
    public async Task PlanLaunch_omits_key_from_process_env_when_no_saved_key(string? apiKey)
    {
        (string tempDir, string codexHome, _) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(apiKey: apiKey);

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(plan.ConfigurationActions.Count).IsEqualTo(1);
            // The shared emission's base-url variable still rides the
            // environment; only the key is omitted.
            await Assert.That(plan.Process.EnvironmentVariables.Count).IsEqualTo(1);
            await Assert.That(plan.Process.EnvironmentVariables)
                .DoesNotContainKey(CodexAdapter.EnvKeyName);
            await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"])
                .IsEqualTo("https://api.example.com/v1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_never_writes_a_dummy_key_value()
    {
        (string tempDir, string codexHome, _) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(apiKey: null);

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);
            string content = ((ConfigFileWriteAction)plan.ConfigurationActions[0]).Content;

            await Assert.That(content.Contains("sk-test", StringComparison.Ordinal)).IsFalse();
            await Assert.That(content.Contains("dummy", StringComparison.Ordinal)).IsFalse();
            // No key saved - none may appear in the spawned process environment either.
            await Assert.That(plan.Process.EnvironmentVariables)
                .DoesNotContainKey(CodexAdapter.EnvKeyName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_refuses_when_provider_block_already_exists()
    {
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
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

            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest();

            LaunchRefusalException? caught = null;
            try
            {
                await adapter.PlanLaunchAsync(request);
            }
            catch (LaunchRefusalException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("model_providers.harnessgateway");
            await Assert.That(caught!.Message).Contains("remove or rename", StringComparison.Ordinal);
            // Refusal happens before any write: the user's file is untouched.
            string after = await File.ReadAllTextAsync(configPath);
            await Assert.That(after).IsEqualTo(userOwned.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_refuses_when_provider_defined_as_table_array()
    {
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            await File.WriteAllTextAsync(configPath, "[[model_providers.harnessgateway]]\nname = \"Mine\"\n");

            CodexAdapter adapter = CreateAdapter(codexHome);

            LaunchRefusalException? caught = null;
            try
            {
                await adapter.PlanLaunchAsync(CreateRequest());
            }
            catch (LaunchRefusalException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            // Refusal happens before any write: the user's file is untouched.
            await Assert.That(await File.ReadAllTextAsync(configPath))
                .IsEqualTo("[[model_providers.harnessgateway]]\nname = \"Mine\"\n");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_refuses_when_provider_defined_inline_under_parent()
    {
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            await File.WriteAllTextAsync(
                configPath,
                "[model_providers]\nharnessgateway = { name = \"Mine\", wire_api = \"responses\" }\n");

            CodexAdapter adapter = CreateAdapter(codexHome);

            LaunchRefusalException? caught = null;
            try
            {
                await adapter.PlanLaunchAsync(CreateRequest());
            }
            catch (LaunchRefusalException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            // Refusal happens before any write: the user's file is untouched.
            await Assert.That(await File.ReadAllTextAsync(configPath))
                .IsEqualTo("[model_providers]\nharnessgateway = { name = \"Mine\", wire_api = \"responses\" }\n");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_consumes_the_planners_emission_in_config_and_environment()
    {
        // A sentinel emission proves the base URL comes from the planner
        // (T007): it lands in the harness-owned TOML block and as the
        // launch's one base-url environment variable, while the key stays
        // under this harness's env_key name - never the planner's slot.
        (string tempDir, string codexHome, string configPath) = CreateCodexHome();
        try
        {
            FakeProtocolEmissionPlanner planner = new()
            {
                CannedEmission = new ProtocolEmission
                {
                    Protocol = ProtocolKind.OpenAiResponses,
                    BaseUrlVariable = "CANNED_BASE_URL",
                    BaseUrlValue = "https://canned.example/v1",
                    ExtraVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CANNED_API_KEY"] = string.Empty,
                    },
                },
            };
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(apiKey: "sk-canned", planner: planner);

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(plan.Process.EnvironmentVariables["CANNED_BASE_URL"])
                .IsEqualTo("https://canned.example/v1");
            await Assert.That(plan.Process.EnvironmentVariables[CodexAdapter.EnvKeyName])
                .IsEqualTo("sk-canned");
            await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("CANNED_API_KEY");

            string content = ((ConfigFileWriteAction)plan.ConfigurationActions[0]).Content;
            await Assert.That(content).Contains("base_url = \"https://canned.example/v1\"");
            await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
            await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
            await Assert.That(planner.PlannedCalls[0].BaseUri)
                .IsEqualTo(new Uri("https://api.example.com/v1"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_from_the_ad_hoc_case_plans_the_ad_hoc_values()
    {
        // The family's other arm: a raw-URL launch reaches the same planner
        // call with its own base URL (T021), and the session key still
        // credentials it under this harness's env_key.
        (string tempDir, string codexHome, _) = CreateCodexHome();
        try
        {
            FakeProtocolEmissionPlanner planner = new();
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest(
                apiKey: "sk-wizard",
                adHoc: true,
                planner: planner);

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);

            await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
            await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
            await Assert.That(planner.PlannedCalls[0].BaseUri)
                .IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"])
                .IsEqualTo("https://api.example.com/v1");
            await Assert.That(plan.Process.EnvironmentVariables[CodexAdapter.EnvKeyName])
                .IsEqualTo("sk-wizard");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PlanLaunch_emits_exactly_one_base_url_variable_and_no_unrelated_protocol_vars()
    {
        // Exactly one base-url variable per launch - this protocol's - and
        // never an ANTHROPIC_* variable beside it (T007).
        (string tempDir, string codexHome, _) = CreateCodexHome();
        try
        {
            CodexAdapter adapter = CreateAdapter(codexHome);
            ResolvedLaunch request = CreateRequest();

            LaunchPlan plan = await adapter.PlanLaunchAsync(request);

            string[] baseUrlVariables =
            [
                .. plan.Process.EnvironmentVariables.Keys.Where(
                    key => key.EndsWith("_BASE_URL", StringComparison.Ordinal)),
            ];
            await Assert.That(baseUrlVariables).IsEquivalentTo(["OPENAI_BASE_URL"]);
            await Assert.That(plan.Process.EnvironmentVariables)
                .DoesNotContainKey("ANTHROPIC_BASE_URL");
            await Assert.That(plan.Process.EnvironmentVariables)
                .DoesNotContainKey("ANTHROPIC_API_KEY");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
