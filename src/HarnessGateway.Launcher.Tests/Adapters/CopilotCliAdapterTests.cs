using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class CopilotCliAdapterTests
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
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_declares_supported_protocols()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();

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
        CopilotCliAdapter adapter = new CopilotCliAdapter((string _, CancellationToken _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter((string _, CancellationToken _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_sets_provider_base_url()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("copilot");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"]).IsEqualTo("https://api.example.com/v1");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_openai()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest(ProtocolKind.OpenAiChat);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("openai");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_anthropic()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest(ProtocolKind.Anthropic);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("anthropic");
    }

    [Test]
    public async Task PlanLaunch_sets_api_key()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest(apiKey: "test-key");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"]).IsEqualTo("test-key");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest(apiKey: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("COPILOT_PROVIDER_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest(model: "gpt-4o");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).Contains("--model");
        await Assert.That(plan.Process.Arguments).Contains("gpt-4o");
    }

    [Test]
    public async Task PlanLaunch_no_config_file_writes()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        LaunchRequest request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        List<ConfigFileWriteAction> fileWrites = [.. plan.ConfigurationActions.OfType<ConfigFileWriteAction>()];
        await Assert.That(fileWrites).IsEmpty();
    }
}
