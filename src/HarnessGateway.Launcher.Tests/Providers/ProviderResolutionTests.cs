using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Providers;

public class ProviderResolutionTests
{
    private static Provider Entry(
        string name,
        string uri = "https://api.example.com/v1",
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? defaultModel = null,
        string? apiKey = null,
        IList<ModelEntry>? models = null) => new()
    {
        Name = name,
        BaseUri = new Uri(uri),
        DefaultProtocol = protocol,
        DefaultModel = defaultModel,
        ApiKey = apiKey,
        Models = models ?? []
    };

    private static ModelEntry Model(string name, ProtocolKind? protocol = null) =>
        new() { Name = name, Protocol = protocol };

    private static LauncherSettings SettingsWith(string defaultPointer, params Provider[] providers) => new()
    {
        Providers = [.. providers],
        DefaultProvider = defaultPointer
    };

    private static LaunchResolution.ProviderCase CaseOf(ProviderResolutionResult result) =>
        (LaunchResolution.ProviderCase)((ProviderResolutionResult.Resolved)result).Launch;

    [Test]
    public async Task Resolve_selects_entry_named_by_provider_flag()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "secondary", null, null, null);

        await Assert.That(CaseOf(result).Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_matches_provider_name_case_insensitively()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "SECONDARY", null, null, null);

        await Assert.That(CaseOf(result).Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_flag_pair_replaces_endpoint_and_protocol_for_the_session()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary"),
            Entry(
                "secondary",
                uri: "https://secondary.example/v1",
                protocol: ProtocolKind.OpenAiResponses,
                defaultModel: "gpt-4o",
                apiKey: "hgkey:secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            "secondary",
            "https://override.example/v1",
            "openai-chat",
            null);

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.BaseUri).IsEqualTo(new Uri("https://override.example/v1"));
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        // Every other field comes from the saved entry - including the key,
        // which is never overridable per launch.
        await Assert.That(providerCase.Provider.Name).IsEqualTo("secondary");
        await Assert.That(providerCase.Provider.DefaultModel).IsEqualTo("gpt-4o");
        await Assert.That(providerCase.Provider.ApiKey).IsEqualTo("hgkey:secondary");
    }

    [Test]
    public async Task Resolve_model_flag_overrides_only_the_model()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary"),
            Entry(
                "secondary",
                uri: "https://secondary.example/v1",
                protocol: ProtocolKind.OpenAiResponses,
                defaultModel: "gpt-4o"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "secondary", null, null, "gpt-5");

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Model).IsEqualTo("gpt-5");
        await Assert.That(providerCase.BaseUri).IsEqualTo(new Uri("https://secondary.example/v1"));
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task Resolve_without_flags_reads_the_default_pointer_only()
    {
        LauncherSettings settings = SettingsWith("secondary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        await Assert.That(CaseOf(result).Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_absent_default_with_entries_fails_without_first_entry_fallback()
    {
        LauncherSettings settings = SettingsWith(string.Empty, Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.AbsentDefaultProvider);
        await Assert.That(failed.Message).Contains("config set defaultProvider");
    }

    [Test]
    public async Task Resolve_dangling_default_pointer_fails_with_pointer_directive()
    {
        LauncherSettings settings = SettingsWith("ghost", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.DanglingDefaultProvider);
        await Assert.That(failed.Message).Contains("unknown provider name 'ghost'");
        await Assert.That(failed.Message).Contains("config set defaultProvider");
    }

    [Test]
    public async Task Resolve_dangling_default_pointer_may_hint_a_close_match()
    {
        // The dangling pointer names a near miss of a saved entry; the hint is
        // best-effort, non-contractual UX and never changes the failure kind.
        LauncherSettings settings = SettingsWith("praimary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.DanglingDefaultProvider);
        await Assert.That(failed.Message).Contains("'primary'");
    }

    [Test]
    public async Task Resolve_without_entries_and_without_pointer_returns_no_provider()
    {
        LauncherSettings settings = SettingsWith(string.Empty);

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        await Assert.That(result).IsTypeOf<ProviderResolutionResult.EmptySettings>();
    }

    [Test]
    public async Task Resolve_unknown_provider_name_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "nope", null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.UnknownProviderName);
        await Assert.That(failed.Message).Contains("unknown provider name 'nope'");
    }

    [Test]
    public async Task Resolve_unknown_provider_name_may_hint_a_close_match_without_changing_the_outcome()
    {
        LauncherSettings settings = SettingsWith("production", Entry("production"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "prod", null, null, null);

        // The hint is best-effort, non-contractual UX; the outcome is the failure.
        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.UnknownProviderName);
        await Assert.That(failed.Message).Contains("unknown provider name 'prod'");
        await Assert.That(failed.Message).Contains("production");
    }

    [Test]
    public async Task Resolve_endpoint_without_protocol_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, "https://api.example.com/v1", null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.EndpointWithoutProtocol);
        await Assert.That(failed.Message).Contains("--endpoint requires --protocol");
    }

    [Test]
    public async Task Resolve_protocol_without_endpoint_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, "openai-chat", null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.ProtocolWithoutEndpoint);
        await Assert.That(failed.Message).Contains("--protocol requires --endpoint");
    }

    [Test]
    public async Task Resolve_invalid_endpoint_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, "not-a-url", "openai-chat", null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.InvalidEndpoint);
        await Assert.That(failed.Message).Contains("not-a-url");
    }

    [Test]
    public async Task Resolve_invalid_protocol_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            null,
            "https://api.example.com/v1",
            "smtp",
            null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.InvalidProtocol);
        await Assert.That(failed.Message).Contains("openai-chat, openai-responses, anthropic");
    }

    [Test]
    public async Task Resolve_raw_url_without_saved_entries_returns_the_ad_hoc_case()
    {
        LauncherSettings settings = SettingsWith(string.Empty);

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "anthropic",
            "claude-sonnet-5");

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        LaunchResolution.AdHocCase adHoc = (LaunchResolution.AdHocCase)resolved.Launch;
        await Assert.That(adHoc.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(adHoc.Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(adHoc.Model).IsEqualTo("claude-sonnet-5");
    }

    [Test]
    public async Task Ad_hoc_case_carries_no_provider_or_key_reference()
    {
        // The type system forbids a keyless launch from reaching
        // consumers that assume a stored provider exists.
        await Assert.That(typeof(LaunchResolution.AdHocCase).GetProperty("Provider")).IsNull();
        await Assert.That(typeof(LaunchResolution.AdHocCase).GetProperty("ApiKey")).IsNull();
        await Assert.That(typeof(LaunchResolution.ProviderCase).GetProperty("Provider")).IsNotNull();
    }

    [Test]
    public async Task Resolve_raw_url_layers_onto_the_resolved_entry_and_keeps_its_key()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", uri: "https://primary.example/v1", apiKey: "hgkey:primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "openai-responses",
            null);

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Provider.Name).IsEqualTo("primary");
        await Assert.That(providerCase.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
        await Assert.That(providerCase.Provider.ApiKey).IsEqualTo("hgkey:primary");
    }

    [Test]
    public async Task Resolve_raw_url_still_reads_the_default_pointer_strictly()
    {
        // A fully explicit raw URL does not bypass a broken profile: the
        // unresolvable default pointer is still the code-8 failure class.
        LauncherSettings settings = SettingsWith("ghost", Entry("primary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "openai-chat",
            null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.DanglingDefaultProvider);
        await Assert.That(failed.Message).Contains("config set defaultProvider");
    }

    [Test]
    public async Task Resolve_model_entry_protocol_override_wins_over_provider_default()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry(
                "primary",
                protocol: ProtocolKind.OpenAiChat,
                defaultModel: "gpt-5",
                models: [Model("gpt-5", ProtocolKind.Anthropic)]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.Anthropic);
        // The session-layered copy carries the effective protocol too: the
        // planner contract extracts (Protocol, BaseUri) from either case, and
        // pre-planner consumers read Provider.DefaultProtocol.
        await Assert.That(providerCase.Provider.DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(providerCase.BaseUri).IsEqualTo(providerCase.Provider.BaseUri);
    }

    [Test]
    public async Task Resolve_model_entry_without_override_inherits_the_provider_default()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry(
                "primary",
                protocol: ProtocolKind.OpenAiResponses,
                defaultModel: "gpt-5",
                models: [Model("gpt-5")]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task Resolve_model_flag_selects_the_listed_entry_and_its_override()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry(
                "primary",
                defaultModel: "gpt-4o",
                models: [Model("gpt-4o"), Model("gpt-5", ProtocolKind.Anthropic)]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, "gpt-5");

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Model).IsEqualTo("gpt-5");
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.Anthropic);
    }

    [Test]
    public async Task Resolve_unlisted_model_is_refused_with_a_close_match_hint()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", defaultModel: "gpt-5", models: [Model("gpt-5")]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "primary", null, null, "gpt-4");

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.UnlistedModel);
        await Assert.That(failed.Message).Contains("'gpt-4'");
        // The best-effort hint surfaces the listed near miss (non-contractual
        // UX, asserted only where a close match deterministically exists).
        await Assert.That(failed.Message).Contains("'gpt-5'");
    }

    [Test]
    public async Task Resolve_default_model_missing_from_the_list_is_refused_like_any_unlisted_model()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", defaultModel: "gpt-4", models: [Model("gpt-5")]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "primary", null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.UnlistedModel);
        await Assert.That(failed.Message).Contains("'gpt-4'");
    }

    [Test]
    public async Task Resolve_empty_models_array_launches_any_model_on_the_default_protocol()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", protocol: ProtocolKind.OpenAiResponses, models: []));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "primary", null, null, "any-model");

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Model).IsEqualTo("any-model");
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task Resolve_non_empty_models_without_any_model_still_launches()
    {
        // Nothing is named, so nothing can be unlisted: the guard fires for a
        // named model absent from the list, never for an absent model.
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", models: [Model("gpt-5")]));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "primary", null, null, null);

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Model).IsNull();
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
    }

    [Test]
    public async Task Resolve_flag_pair_skips_the_unlisted_model_guard()
    {
        // The flag pair replaces endpoint and protocol wholesale for
        // the session - stored bindings are ignored and the unlisted-model
        // guard does not apply to the overridden launch.
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry(
                "primary",
                defaultModel: "gpt-5",
                models: [Model("gpt-5", ProtocolKind.OpenAiResponses)]));

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            "primary",
            "https://override.example/v1",
            "anthropic",
            "gpt-4");

        LaunchResolution.ProviderCase providerCase = CaseOf(result);
        await Assert.That(providerCase.Model).IsEqualTo("gpt-4");
        await Assert.That(providerCase.Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(providerCase.BaseUri).IsEqualTo(new Uri("https://override.example/v1"));
    }

    [Test]
    public async Task ResolveEntry_skips_the_launch_only_unlisted_guard_for_management_paths()
    {
        // Management and read paths (config pointer writes, key lifecycle, the
        // wizard's prefill) resolve entries without the launch-time guard: a
        // broken DefaultModel must not block pointer writes, key lifecycle, or
        // removing the provider that carries it.
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", defaultModel: "gpt-4", models: [Model("gpt-5")]));

        ProviderResolutionResult management = ProviderResolution.ResolveEntry(settings, "primary");
        await Assert.That(CaseOf(management).Provider.Name).IsEqualTo("primary");

        ProviderResolutionResult launch = ProviderResolution.Resolve(settings, "primary", null, null, null);
        await Assert.That(launch).IsTypeOf<ProviderResolutionResult.Failed>();
    }
}
