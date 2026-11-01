using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class ClaudeCodeAdapterTests
{
    private static ClaudeCodeAdapter CreateAdapter(bool available = true)
    {
        return new ClaudeCodeAdapter(
            (_, _) => Task.FromResult(available));
    }

    private static ResolvedLaunch CreateRequest(
        string? model = "claude-sonnet-5",
        string? apiKey = "sk-test",
        string baseUri = "https://api.example.com/v1/")
    {
        return new ResolvedLaunch(
            new Provider
            {
                Name = "test",
                BaseUri = new Uri(baseUri),
                DefaultProtocol = ProtocolKind.Anthropic,
                ApiKey = apiKey
            },
            Model: model,
            Path.GetTempPath());
    }

    [Test]
    public async Task Metadata_declares_name_kind_and_no_model_requirement()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.Name).IsEqualTo("claude-code");
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(adapter.Metadata.RequiresModel).IsFalse();
    }

    [Test]
    public async Task Metadata_install_hint_covers_npm_and_disabled_harness_behaviours()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.InstallHint).Contains("npm");
        // Known disabled behaviours are surfaced in the adapter's hint text: a
        // non-first-party base URL turns off MCP tool search and Remote Control
        // inside Claude Code itself - a harness consequence, not a launcher action.
        await Assert.That(adapter.Metadata.InstallHint).Contains("non-first-party");
        await Assert.That(adapter.Metadata.InstallHint).Contains("MCP (Model Context Protocol)");
        await Assert.That(adapter.Metadata.InstallHint).Contains("Remote Control");
        await Assert.That(adapter.Metadata.InstallHint).Contains("not an action by the launcher");
    }

    [Test]
    public async Task Metadata_detection_command_probes_claude()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.DetectionCommand).IsEqualTo("claude --version");
    }

    [Test]
    public async Task Metadata_supports_only_anthropic()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.SupportedProtocols)
            .IsEquivalentTo([ProtocolKind.Anthropic]);
    }

    [Test]
    public async Task Metadata_has_no_config_file_candidates()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.ConfigFileCandidates).IsEmpty();
    }

    [Test]
    public async Task IsAvailable_returns_true_when_on_path()
    {
        ClaudeCodeAdapter adapter = CreateAdapter(available: true);
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        ClaudeCodeAdapter adapter = CreateAdapter(available: false);
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_sets_base_url_on_the_spawned_process()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: null, model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("claude");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_BASE_URL"])
            .IsEqualTo("https://api.example.com/v1");
    }

    [Test]
    public async Task PlanLaunch_uses_auth_token_never_api_key()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: "sk-live");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        // Token variable choice, locked: ANTHROPIC_AUTH_TOKEN carries the saved
        // key...
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_AUTH_TOKEN");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"])
            .IsEqualTo("sk-live");
        // ...and ANTHROPIC_API_KEY must never be used - it makes Claude Code raise
        // an interactive approval prompt.
        await Assert.That(plan.Process.EnvironmentVariables.ContainsKey("ANTHROPIC_API_KEY"))
            .IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task PlanLaunch_omits_token_variable_when_no_saved_key(string? apiKey)
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: apiKey, model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        // No key saved means no token action at all - and no dummy value either:
        // the only configured variable is the base URL.
        await Assert.That(plan.Process.EnvironmentVariables.ContainsKey("ANTHROPIC_AUTH_TOKEN"))
            .IsFalse();
        await Assert.That(plan.Process.EnvironmentVariables.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_model_env_and_cli_argument_action()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_MODEL"])
            .IsEqualTo("claude-sonnet-5");

        List<CliArgumentAction> cliArgs = [.. plan.ConfigurationActions.OfType<CliArgumentAction>()];
        await Assert.That(cliArgs).Count().IsEqualTo(1);
        await Assert.That(cliArgs[0].Argument).IsEqualTo("--model");
        await Assert.That(cliArgs[0].Value).IsEqualTo("claude-sonnet-5");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task PlanLaunch_without_model_omits_model_env_and_cli_argument(string? model)
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(model: model);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables.ContainsKey("ANTHROPIC_MODEL"))
            .IsFalse();
        await Assert.That(plan.ConfigurationActions).IsEmpty();
    }

    [Test]
    public async Task PlanLaunch_never_plans_a_config_file_write()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        List<ConfigFileWriteAction> fileWrites = [.. plan.ConfigurationActions.OfType<ConfigFileWriteAction>()];
        await Assert.That(fileWrites).IsEmpty();
        // The whole action surface is the optional --model argument: env-var
        // configuration rides on Process.EnvironmentVariables, never on the
        // action family, and a config file write is never planned.
        await Assert.That(plan.ConfigurationActions.All(action => action is CliArgumentAction))
            .IsTrue();
    }
}
