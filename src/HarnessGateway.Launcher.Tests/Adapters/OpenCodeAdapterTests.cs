using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class OpenCodeAdapterTests
{
    private static ResolvedLaunch CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? model = "gpt-4",
        string? apiKey = "sk-test",
        bool adHoc = false,
        IProtocolEmissionPlanner? planner = null)
    {
        Uri baseUri = new("https://api.example.com/v1");
        Provider provider = new()
        {
            Name = "test",
            BaseUri = baseUri,
            DefaultProtocol = protocol,
            ApiKey = apiKey
        };

        // The provider case carries the stored entry; the ad-hoc case is the
        // keyless raw-URL arm of the family. The launch-scoped key rides the
        // payload either way - a wizard session key can credential an
        // ad-hoc launch.
        LaunchResolution launch = adHoc
            ? new LaunchResolution.AdHocCase(baseUri, protocol, model)
            : new LaunchResolution.ProviderCase(provider, model, protocol);

        return new ResolvedLaunch(
            launch,
            apiKey,
            Path.GetTempPath(),
            planner ?? new ProtocolEmissionPlanner());
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
        ResolvedLaunch request = CreateRequest(ProtocolKind.OpenAiChat);

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
        ResolvedLaunch request = CreateRequest(ProtocolKind.Anthropic);

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
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

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
        ResolvedLaunch request = CreateRequest(ProtocolKind.Anthropic, model: "claude-sonnet-5");

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
        ResolvedLaunch request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENCODE_CONFIG_CONTENT");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_consumes_the_planners_emission_for_the_resolved_case()
    {
        // A sentinel emission proves the adapter merges whatever the
        // planner returns: it computes no protocol and hardcodes no
        // variable names of its own (T018, T007).
        FakeProtocolEmissionPlanner planner = new()
        {
            CannedEmission = new ProtocolEmission
            {
                Protocol = ProtocolKind.OpenAiChat,
                BaseUrlVariable = "CANNED_BASE_URL",
                BaseUrlValue = "https://canned.example/v1",
                ExtraVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CANNED_API_KEY"] = string.Empty,
                },
            },
        };
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        ResolvedLaunch request = CreateRequest(planner: planner, apiKey: "sk-canned");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["CANNED_BASE_URL"])
            .IsEqualTo("https://canned.example/v1");
        // The protocol's key slot is filled from the launch-scoped secret.
        await Assert.That(plan.Process.EnvironmentVariables["CANNED_API_KEY"])
            .IsEqualTo("sk-canned");
        // No hardcoded canonical spelling sneaks in beside the planner's.
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
        // The plan was made from the resolved case's own values.
        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task PlanLaunch_from_the_ad_hoc_case_plans_the_ad_hoc_values()
    {
        // The family's other arm: a raw-URL launch reaches the same planner
        // call with its own protocol and base URL (T021), and the session
        // key still credentials it.
        FakeProtocolEmissionPlanner planner = new();
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        ResolvedLaunch request = CreateRequest(
            protocol: ProtocolKind.Anthropic,
            model: null,
            apiKey: "sk-wizard",
            adHoc: true,
            planner: planner);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1"));
        // The real mapping, reached through the interface.
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["ANTHROPIC_API_KEY"])
            .IsEqualTo("sk-wizard");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
    }

    [Test]
    [Arguments(ProtocolKind.OpenAiChat, "OPENAI_BASE_URL")]
    [Arguments(ProtocolKind.Anthropic, "ANTHROPIC_BASE_URL")]
    public async Task PlanLaunch_emits_exactly_one_base_url_variable_from_the_planned_protocol(
        ProtocolKind protocol, string expectedVariable)
    {
        // Exactly one base-url variable per launch - never both protocol
        // spellings beside each other, and never the other protocol's key
        // variable either (T007).
        OpenCodeAdapter adapter = new OpenCodeAdapter();
        ResolvedLaunch request = CreateRequest(protocol: protocol);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        string[] baseUrlVariables =
        [
            .. plan.Process.EnvironmentVariables.Keys.Where(
                key => key.EndsWith("_BASE_URL", StringComparison.Ordinal)),
        ];
        await Assert.That(baseUrlVariables).IsEquivalentTo([expectedVariable]);
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey(expectedVariable);

        string unrelatedKeyVariable = protocol == ProtocolKind.OpenAiChat
            ? "ANTHROPIC_API_KEY"
            : "OPENAI_API_KEY";
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey(unrelatedKeyVariable);
    }
}
