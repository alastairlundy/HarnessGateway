using TUnit;
using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Tests.Adapters;

public class KiloAdapterTests
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
        var adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        var adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        var adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        var adapter = new KiloAdapter((string _, CancellationToken _) => Task.FromResult<string?>("kilo"));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        var adapter = new KiloAdapter((string _, CancellationToken _) => Task.FromResult<string?>(null));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        var adapter = new KiloAdapter();
        var request = CreateRequest(ProtocolKind.OpenAiChat);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("kilo");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_anthropic_sets_env_vars()
    {
        var adapter = new KiloAdapter();
        var request = CreateRequest(ProtocolKind.Anthropic);

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        var adapter = new KiloAdapter();
        var request = CreateRequest(model: "claude-3-opus");

        var plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.Arguments).Contains("--model");
        await Assert.That(plan.Process.Arguments).Contains("claude-3-opus");
    }
}
