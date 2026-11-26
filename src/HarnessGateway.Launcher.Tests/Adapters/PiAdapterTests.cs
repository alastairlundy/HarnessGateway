using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class PiAdapterTests
{
    private static ResolvedLaunch CreateRequest(
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? model = "gpt-4",
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
        // keyless raw-URL arm of the family. The launch-scoped key goes on the
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
        PiAdapter adapter = new PiAdapter((_, _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        PiAdapter adapter = new PiAdapter((_, _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_openai_sets_env_vars()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("pi");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_responses_drops_a_trailing_responses_segment()
    {
        // Pi's openai-responses family posts to {baseURL}/responses itself
        // (the OpenAI SDK it constructs joins the resource path onto the
        // base URL), so an endpoint that already ends in the segment must
        // reach it without it.
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(
            ProtocolKind.OpenAiResponses,
            baseUriText: "https://api.example.com/v1/responses");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"])
            .IsEqualTo("https://api.example.com/v1");
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
    public async Task PlanNonInteractive_puts_the_prompt_after_pis_print_flag()
    {
        // pi -p "<prompt>": -p/--print is pi's boolean print-mode flag and
        // the prompt is the message positional after it - not the flag's
        // own value.
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanNonInteractiveAsync(request, "fix the failing test");

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("pi");
        await Assert.That(plan.Process.ArgumentList).IsEquivalentTo(["-p", "fix the failing test"]);
    }

    [Test]
    public async Task PlanNonInteractive_keeps_the_model_flag()
    {
        // The one-shot plan differs from the interactive one only by the
        // print flag and the prompt: the model still rides --model.
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: "claude-sonnet-5");

        LaunchPlan plan = await adapter.PlanNonInteractiveAsync(request, "fix the failing test");

        await Assert.That(plan.Process.ArgumentList)
            .IsEquivalentTo(["--model", "claude-sonnet-5", "-p", "fix the failing test"]);
    }

    [Test]
    public async Task PlanNonInteractive_keeps_the_interactive_env_vars()
    {
        // -p changes how pi reads its input, not how it is configured:
        // the emission's base URL and key reach the child exactly as the
        // interactive launch plans them.
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanNonInteractiveAsync(request, "summarize the git history");

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("OPENAI_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_omits_the_print_flag()
    {
        // The interactive plan never carries -p: only the one-shot member
        // adds it, so an interactive launch can't be misread as print mode.
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.ArgumentList).DoesNotContain("-p");
    }

    [Test]
    public async Task PlanLaunch_warn_and_launch_when_no_model()
    {
        PiAdapter adapter = new PiAdapter();
        ResolvedLaunch request = CreateRequest(model: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("pi");
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
        PiAdapter adapter = new PiAdapter();
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
        PiAdapter adapter = new PiAdapter();
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
        PiAdapter adapter = new PiAdapter();
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
}
