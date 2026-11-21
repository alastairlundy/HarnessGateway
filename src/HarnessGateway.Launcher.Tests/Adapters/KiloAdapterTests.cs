using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class KiloAdapterTests
{
    private static ResolvedLaunch CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? model = "gpt-5",
        string? apiKey = "sk-test",
        bool adHoc = false,
        IProtocolEmissionPlanner? planner = null,
        string baseUriText = "https://api.example.com/v1")
    {
        Uri baseUri = new(baseUriText);
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
        KiloAdapter adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Cli);
    }

    [Test]
    public async Task Metadata_requires_model()
    {
        KiloAdapter adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.RequiresModel).IsTrue();
    }

    [Test]
    public async Task Metadata_has_install_hint()
    {
        KiloAdapter adapter = new KiloAdapter();
        await Assert.That(adapter.Metadata.InstallHint).IsNotEmpty();
    }

    [Test]
    public async Task Metadata_declares_supported_protocols()
    {
        KiloAdapter adapter = new KiloAdapter();

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
        KiloAdapter adapter = new KiloAdapter((_, _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        KiloAdapter adapter = new KiloAdapter((_, _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("kilo");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_anthropic_sets_env_vars()
    {
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest(ProtocolKind.Anthropic);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("ANTHROPIC_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_responses_drops_a_trailing_responses_segment()
    {
        // Kilo's openai-responses protocol joins "/responses" onto the base
        // URL itself, so an endpoint that already ends in the segment must
        // reach it without it.
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest(
            ProtocolKind.OpenAiResponses,
            baseUriText: "https://api.example.com/v1/responses");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"])
            .IsEqualTo("https://api.example.com/v1");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.ArgumentList).Contains("--model");
        await Assert.That(plan.Process.ArgumentList).Contains("openai/claude-sonnet-5");
    }

    [Test]
    public async Task PlanLaunch_consumes_the_planners_emission_for_the_resolved_case()
    {
        // A sentinel emission proves the adapter merges whatever the
        // planner returns: it computes no protocol and hardcodes no
        // variable names of its own.
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
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest(planner: planner, apiKey: "sk-canned");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["CANNED_BASE_URL"])
            .IsEqualTo("https://canned.example/v1");
        await Assert.That(plan.Process.EnvironmentVariables["CANNED_API_KEY"])
            .IsEqualTo("sk-canned");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task PlanLaunch_from_the_ad_hoc_case_plans_the_ad_hoc_values()
    {
        // The family's other arm: a raw-URL launch reaches the same planner
        // call with its own protocol and base URL, and the session
        // key still credentials it.
        FakeProtocolEmissionPlanner planner = new();
        KiloAdapter adapter = new KiloAdapter();
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
        // variable either.
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest(protocol: protocol);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        string[] baseUrlVariables =
        [
            .. plan.Process.EnvironmentVariables.Keys.Where(
                key => key.EndsWith("_BASE_URL", StringComparison.Ordinal)),
        ];
        await Assert.That(baseUrlVariables).IsEquivalentTo([expectedVariable]);

        string unrelatedKeyVariable = protocol == ProtocolKind.OpenAiChat
            ? "ANTHROPIC_API_KEY"
            : "OPENAI_API_KEY";
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey(unrelatedKeyVariable);
    }

    [Test]
    public async Task PlanNonInteractive_runs_the_run_subcommand_with_model_and_prompt()
    {
        KiloAdapter adapter = new KiloAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanNonInteractiveAsync(request, "fix the failing test");

        // `kilo run` takes the message as its positional and its own
        // -m/--model flag, so the model moves into the run subcommand's
        // options rather than the global slot the interactive launch uses.
        await Assert.That(plan.Process.ArgumentList).IsEquivalentTo(
            ["run", "--model", "openai/gpt-5", "fix the failing test"]);
    }
}
