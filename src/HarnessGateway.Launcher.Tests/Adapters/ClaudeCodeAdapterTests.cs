using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class ClaudeCodeAdapterTests
{
    private static ClaudeCodeAdapter CreateAdapter(bool available = true)
    {
        return new ClaudeCodeAdapter(
            (_, _) => Task.FromResult(
                available ? AvailabilityResult.Found : AvailabilityResult.Absent));
    }

    private static ResolvedLaunch CreateRequest(
        string? model = "claude-sonnet-5",
        string? apiKey = "sk-test",
        string baseUri = "https://api.example.com/v1/",
        bool adHoc = false,
        IProtocolEmissionPlanner? planner = null)
    {
        Uri baseAddress = new(baseUri);
        Provider provider = new()
        {
            Name = "test",
            BaseUri = baseAddress,
            DefaultProtocol = ProtocolKind.Anthropic,
            ApiKey = apiKey
        };

        // The provider case carries the stored entry; the ad-hoc case is the
        // keyless raw-URL arm of the family. The launch-scoped key goes on the
        // payload either way - a wizard session key can credential an
        // ad-hoc launch.
        LaunchResolution launch = adHoc
            ? new LaunchResolution.AdHocCase(baseAddress, ProtocolKind.Anthropic, model)
            : new LaunchResolution.ProviderCase(provider, model, ProtocolKind.Anthropic);

        return new ResolvedLaunch(
            launch,
            apiKey,
            Path.GetTempPath(),
            planner ?? new ProtocolEmissionPlanner());
    }

    [Test]
    public async Task Metadata_declares_name_kind_and_no_model_requirement()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();

        await Assert.That(adapter.Metadata.Name).IsEqualTo("Claude Code");
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
    public async Task IsAvailable_returns_found_when_on_path()
    {
        ClaudeCodeAdapter adapter = CreateAdapter(available: true);
        await Assert.That(await adapter.IsAvailableAsync()).IsEqualTo(AvailabilityResult.Found);
    }

    [Test]
    public async Task IsAvailable_returns_absent_when_not_on_path()
    {
        ClaudeCodeAdapter adapter = CreateAdapter(available: false);
        await Assert.That(await adapter.IsAvailableAsync()).IsEqualTo(AvailabilityResult.Absent);
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
        // configuration goes on Process.EnvironmentVariables, never on the
        // action family, and a config file write is never planned.
        await Assert.That(plan.ConfigurationActions.All(action => action is CliArgumentAction))
            .IsTrue();
    }

    [Test]
    public async Task PlanLaunch_consumes_the_planners_emission_and_renames_the_key_slot()
    {
        // A sentinel emission proves the base-url variable comes from the
        // planner, while the key slot keeps this harness's own
        // rename: ANTHROPIC_AUTH_TOKEN, never the planner's
        // ANTHROPIC_API_KEY spelling (harness-owned per the
        // planner's contract).
        FakeProtocolEmissionPlanner planner = new()
        {
            CannedEmission = new ProtocolEmission
            {
                Protocol = ProtocolKind.Anthropic,
                BaseUrlVariable = "CANNED_BASE_URL",
                BaseUrlValue = "https://canned.example/v1",
                ExtraVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CANNED_API_KEY"] = string.Empty,
                },
            },
        };
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: "sk-canned", planner: planner);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["CANNED_BASE_URL"])
            .IsEqualTo("https://canned.example/v1");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"])
            .IsEqualTo("sk-canned");
        // The planner's key slot is deliberately not filled under its own
        // name - that variable makes Claude Code raise an approval prompt.
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("CANNED_API_KEY");
        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1/"));
    }

    [Test]
    public async Task PlanNonInteractive_puts_the_prompt_on_argv_with_claudes_print_flag()
    {
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanNonInteractiveAsync(request, "fix the failing test");

        // -p "<prompt>" is claude's own one-shot spelling; the prompt stays
        // one argv element whatever it contains.
        await Assert.That(plan.Process.ArgumentList).IsEquivalentTo(["-p", "fix the failing test"]);
        // One-shot changes argv only: base URL, token, and model plan exactly
        // as the interactive launch plans them.
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_BASE_URL"])
            .IsEqualTo("https://api.example.com/v1");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_MODEL"])
            .IsEqualTo("claude-sonnet-5");
    }

    [Test]
    public async Task PlanLaunch_from_the_ad_hoc_case_plans_the_ad_hoc_values()
    {
        // The family's other arm: a raw-URL launch reaches the same planner
        // call with its own base URL, and the session key still
        // credentials it under the harness's rename.
        FakeProtocolEmissionPlanner planner = new();
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest(
            model: null,
            apiKey: "sk-wizard",
            adHoc: true,
            planner: planner);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1/"));
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"])
            .IsEqualTo("sk-wizard");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
    }

    [Test]
    public async Task PlanLaunch_emits_exactly_one_base_url_variable_and_no_unrelated_protocol_vars()
    {
        // Exactly one base-url variable per launch - this harness's own -
        // and never an OPENAI_* variable or the planner's API-key spelling
        // beside it.
        ClaudeCodeAdapter adapter = CreateAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        string[] baseUrlVariables =
        [
            .. plan.Process.EnvironmentVariables.Keys.Where(
                key => key.EndsWith("_BASE_URL", StringComparison.Ordinal)),
        ];
        await Assert.That(baseUrlVariables).IsEquivalentTo(["ANTHROPIC_BASE_URL"]);
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("ANTHROPIC_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_API_KEY");
    }
}
