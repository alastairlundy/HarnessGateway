using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class CopilotCliAdapterTests
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
        CopilotCliAdapter adapter = new CopilotCliAdapter((_, _) => Task.FromResult(true));
        await Assert.That(adapter.IsAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task IsAvailable_returns_false_when_not_on_path()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter((_, _) => Task.FromResult(false));
        await Assert.That(adapter.IsAvailableAsync()).IsFalse();
    }

    [Test]
    public async Task PlanLaunch_sets_provider_base_url()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.TargetFilePath).IsEqualTo("copilot");
        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"]).IsEqualTo("https://api.example.com/v1");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_openai()
    {
        CopilotCliAdapter adapter = new();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("openai");
    }

    [Test]
    public async Task PlanLaunch_sets_provider_type_for_anthropic()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest(ProtocolKind.Anthropic);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"]).IsEqualTo("anthropic");
    }

    [Test]
    public async Task PlanLaunch_sets_api_key()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: "test-key");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).ContainsKey("COPILOT_PROVIDER_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"]).IsEqualTo("test-key");
    }

    [Test]
    public async Task PlanLaunch_no_api_key_omits_env_var()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest(apiKey: null);

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("COPILOT_PROVIDER_API_KEY");
    }

    [Test]
    public async Task PlanLaunch_with_model_sets_cli_args()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest(model: "gpt-4o");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.ArgumentList).Contains("--model");
        await Assert.That(plan.Process.ArgumentList).Contains("gpt-4o");
    }

    [Test]
    public async Task PlanLaunch_no_config_file_writes()
    {
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        List<ConfigFileWriteAction> fileWrites = [.. plan.ConfigurationActions.OfType<ConfigFileWriteAction>()];
        await Assert.That(fileWrites).IsEmpty();
    }

    [Test]
    public async Task PlanLaunch_consumes_the_planners_emission_through_its_provider_block()
    {
        // A sentinel emission proves the adapter takes its values from the
        // planner: the base URL and protocol land under this harness's own
        // provider-block names (harness-owned, adapter-side), and
        // the planner's canonical spellings never appear beside them.
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
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest(planner: planner, apiKey: "sk-canned");

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"])
            .IsEqualTo("https://canned.example/v1");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"])
            .IsEqualTo("openai");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"])
            .IsEqualTo("sk-canned");
        // The emission's own names are not this harness's surface.
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("CANNED_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("CANNED_API_KEY");
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
        CopilotCliAdapter adapter = new CopilotCliAdapter();
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
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_TYPE"])
            .IsEqualTo("anthropic");
        await Assert.That(plan.Process.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"])
            .IsEqualTo("sk-wizard");
        await Assert.That(plan.Process.EnvironmentVariables).DoesNotContainKey("OPENAI_BASE_URL");
    }

    [Test]
    public async Task PlanLaunch_emits_exactly_one_base_url_variable_and_no_unrelated_protocol_vars()
    {
        // Exactly one base-url variable per launch - this harness's own -
        // and never a variable of an unrelated protocol beside it.
        CopilotCliAdapter adapter = new CopilotCliAdapter();
        ResolvedLaunch request = CreateRequest();

        LaunchPlan plan = await adapter.PlanLaunchAsync(request);

        string[] baseUrlVariables =
        [
            .. plan.Process.EnvironmentVariables.Keys.Where(
                key => key.EndsWith("_BASE_URL", StringComparison.Ordinal)),
        ];
        await Assert.That(baseUrlVariables).IsEquivalentTo(["COPILOT_PROVIDER_BASE_URL"]);
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey("OPENAI_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey("ANTHROPIC_BASE_URL");
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey("OPENAI_API_KEY");
        await Assert.That(plan.Process.EnvironmentVariables)
            .DoesNotContainKey("ANTHROPIC_API_KEY");
    }
}
