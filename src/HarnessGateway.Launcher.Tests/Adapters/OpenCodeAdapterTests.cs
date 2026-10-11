using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class OpenCodeAdapterTests
{
    private static LaunchRequest CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? model = "gpt-4",
        string? apiKey = "sk-test")
    {
        return new LaunchRequest(
            new Backend
            {
                Name = "test",
                BaseUri = new Uri("https://api.example.com/v1"),
                Protocol = protocol,
                ApiKey = apiKey
            },
            Model: model,
            Path.GetTempPath());
    }

    [Test]
    public async Task Metadata_declares_cli_kind()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_has_detection_command()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.DetectionCommand).IsNotNull();
    }

    [Test]
    public async Task Metadata_has_config_file_candidates()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.ConfigFileCandidates).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_declares_supported_protocols()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();

        await Assert.That(adapter.Metadata.SupportedProtocols).IsEquivalentTo(
        [
            ProtocolKind.OpenAiChat,
                ProtocolKind.OpenAiResponses,
                ProtocolKind.Anthropic
        ]);
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter((string _, CancellationToken _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter((string _, CancellationToken _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(ProtocolKind.OpenAiChat);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.ConfigurationActions).IsEmpty();
        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("opencode");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"]).IsEqualTo("https://api.example.com/v1");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_anthropic_sets_env_vars()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(ProtocolKind.Anthropic);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_inline_config_env_var()
    {
        // opencode v2 rejects a top-level --model flag, so the model rides on
        // OPENCODE_CONFIG_CONTENT and the plan stays flag-free.
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).IsEqualTo("");
        string config = plan.Process.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"];
        await Assert.That(config).Contains("\"model\":\"openai/claude-sonnet-5\"");
        // The endpoint override and the model registration land in the
        // provider entry; the key is referenced through the env, never inlined.
        await Assert.That(config).Contains("\"baseURL\":\"https://api.example.com/v1\"");
        await Assert.That(config).Contains("\"claude-sonnet-5\":{\"name\":\"claude-sonnet-5\"}");
        await Assert.That(config).Contains("\"apiKey\":\"{env:OPENAI_API_KEY}\"");
        await Assert.That(config).DoesNotContain("sk-test");
    }

    [Test]
    public async Task PlanLaunch_anthropic_model_uses_anthropic_provider()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(ProtocolKind.Anthropic, model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        string config = plan.Process.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"];
        await Assert.That(config).Contains("\"model\":\"anthropic/claude-sonnet-5\"");
        await Assert.That(config).Contains("\"apiKey\":\"{env:ANTHROPIC_API_KEY}\"");
        await Assert.That(config).DoesNotContain("openai/claude-sonnet-5");
    }

    [Test]
    public async Task PlanLaunch_without_model_omits_config_env_var()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENCODE_CONFIG_CONTENT");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        LaunchRequest request = CreateRequest(apiKey: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_API_KEY");
    }
}
