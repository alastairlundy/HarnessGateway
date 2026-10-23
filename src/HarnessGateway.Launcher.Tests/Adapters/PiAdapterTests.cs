using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class PiAdapterTests
{
    private static ResolvedLaunch CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? model = "gpt-4",
        string? apiKey = "sk-test")
    {
        return new ResolvedLaunch(
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
        PiAdapter adapter = new PiAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_does_not_require_model()
    {
        PiAdapter adapter = new PiAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsFalse();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        PiAdapter adapter = new PiAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_declares_supported_protocols()
    {
        PiAdapter adapter = new PiAdapter();

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
        PiAdapter adapter = new PiAdapter((string _, CancellationToken _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        PiAdapter adapter = new PiAdapter((string _, CancellationToken _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(ProtocolKind.OpenAiChat);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("pi");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_anthropic_sets_env_vars()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(ProtocolKind.Anthropic);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.ArgumentList).Contains("--model");
        await Assert.That(plan.Process.ArgumentList).Contains("claude-sonnet-5");
    }

    [Test]
    public async Task PlanLaunch_without_model_omits_model_arg()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.ArgumentList).DoesNotContain("--model");
    }

    [Test]
    public async Task PlanLaunch_no_config_file_writes()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        List<ConfigFileWriteAction> fileWrites = [.. plan.ConfigurationActions.OfType<ConfigFileWriteAction>()];
        await Assert.That(fileWrites).IsEmpty();
    }

    [Test]
    public async Task PlanLaunch_warn_and_launch_when_no_model()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("pi");
    }
}
