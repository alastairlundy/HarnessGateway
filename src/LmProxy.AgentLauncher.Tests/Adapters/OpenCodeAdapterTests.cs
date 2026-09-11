using TUnit;
using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Tests.Adapters;

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
            WorkingDirectory: Path.GetTempPath());
    }

    [Test]
    public async Task Metadata_declares_cli_kind()
    {
        var adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        var adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        var adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_has_detection_command()
    {
        var adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.DetectionCommand).IsNotNull();
    }

    [Test]
    public async Task Metadata_has_config_file_candidates()
    {
        var adapter = new OpenCodeAdapter();
        await Assert.That(adapter.Metadata.ConfigFileCandidates).IsNotEmpty();
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        var adapter = new OpenCodeAdapter((string _, CancellationToken _) => Task.FromResult<string?>("opencode"));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        var adapter = new OpenCodeAdapter((string _, CancellationToken _) => Task.FromResult<string?>(null));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        var adapter = new OpenCodeAdapter();
        var request = CreateRequest(ProtocolKind.OpenAiChat);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.ConfigurationActions).IsEmpty();
        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("opencode");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"]).IsEqualTo("https://api.example.com/v1");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_anthropic_sets_env_vars()
    {
        var adapter = new OpenCodeAdapter();
        var request = CreateRequest(ProtocolKind.Anthropic);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        var adapter = new OpenCodeAdapter();
        var request = CreateRequest(model: "claude-sonnet-5");

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).Contains("--model");
        await Assert.That(plan.Process.Arguments).Contains("claude-sonnet-5");
    }

    [Test]
    public async Task PlanLaunch_without_model_omits_model_arg()
    {
        var adapter = new OpenCodeAdapter();
        var request = CreateRequest(model: null);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).DoesNotContain("--model");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        var adapter = new OpenCodeAdapter();
        var request = CreateRequest(apiKey: null);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_API_KEY");
    }
}
