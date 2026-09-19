using HarnessGateway.Launcher.Core.Adapters;
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
        var adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        var adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        var adapter = new CopilotCliAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        var adapter = new CopilotCliAdapter((string _, CancellationToken _) => Task.FromResult<string?>("copilot"));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        var adapter = new CopilotCliAdapter((string _, CancellationToken _) => Task.FromResult<string?>(null));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_sets_provider_base_url()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest();

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("copilot");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"]).IsEqualTo("https://api.example.com/v1");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_openai()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest(ProtocolKind.OpenAiChat);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("openai");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_anthropic()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest(ProtocolKind.Anthropic);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("anthropic");
    }

    [Test]
    public async Task PlanLaunch_sets_api_key()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest(apiKey: "test-key");

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"]).IsEqualTo("test-key");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest(apiKey: null);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("COPILOT_PROVIDER_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest(model: "gpt-4o");

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).Contains("--model");
        await Assert.That(plan.Process.Arguments).Contains("gpt-4o");
    }

    [Test]
    public async Task PlanLaunch_no_config_file_writes()
    {
        var adapter = new CopilotCliAdapter();
        var request = CreateRequest();

        var plan = await adapter.PlanLaunchAsync(request);

        var fileWrites = plan.ConfigurationActions.OfType<ConfigFileWriteAction>().ToList();
        await Assert.That(fileWrites).IsEmpty();
    }
}
