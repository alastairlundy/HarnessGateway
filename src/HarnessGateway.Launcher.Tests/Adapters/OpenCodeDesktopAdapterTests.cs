using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

/// <summary>
/// The GUI sibling plans through the same two-case family and the same
/// shared emission as the CLI adapter it mirrors: only its
/// launch target and declared GUI members differ.
/// </summary>
public class OpenCodeDesktopAdapterTests
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
    public async Task Metadata_declares_gui_kind()
    {
        OpenCodeDesktopAdapter adapter = new OpenCodeDesktopAdapter();
        await Assert.That(adapter.Metadata.Kind).IsEqualTo(HarnessKind.Gui);
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
        OpenCodeDesktopAdapter adapter = new OpenCodeDesktopAdapter();
        ResolvedLaunch request = CreateRequest(planner: planner, apiKey: "sk-canned");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["CANNED_BASE_URL"])
            .IsEqualTo("https://canned.example/v1");
        await Assert.That(plan.Process.EnvironmentVariables["CANNED_API_KEY"])
            .IsEqualTo("sk-canned");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
        // The inline session config consumes the same emission value.
        string config = plan.Process.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"];
        await Assert.That(config).Contains("\"baseURL\":\"https://canned.example/v1\"");
        await Assert.That(planner.PlannedCalls.Count).IsEqualTo(1);
        await Assert.That(planner.PlannedCalls[0].Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(planner.PlannedCalls[0].BaseUri)
            .IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task PlanLaunch_responses_drops_a_trailing_responses_segment()
    {
        // Same engine as the CLI sibling: the AI SDK posts Responses-API
        // requests to {baseURL}/responses itself, so the segment must not
        // ride the base URL it is handed - the env var and the inline
        // config carry the trimmed value.
        OpenCodeDesktopAdapter adapter = new OpenCodeDesktopAdapter();
        ResolvedLaunch request = CreateRequest(
            ProtocolKind.OpenAiResponses,
            baseUriText: "https://api.example.com/v1/responses");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["OPENAI_BASE_URL"])
            .IsEqualTo("https://api.example.com/v1");
        string config = plan.Process.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"];
        await Assert.That(config).Contains("\"baseURL\":\"https://api.example.com/v1\"");
    }

    [Test]
    public async Task PlanLaunch_from_the_ad_hoc_case_plans_the_ad_hoc_values()
    {
        // The family's other arm: a raw-URL launch reaches the same planner
        // call with its own protocol and base URL, and the session
        // key still credentials it.
        FakeProtocolEmissionPlanner planner = new();
        OpenCodeDesktopAdapter adapter = new OpenCodeDesktopAdapter();
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
        OpenCodeDesktopAdapter adapter = new OpenCodeDesktopAdapter();
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
