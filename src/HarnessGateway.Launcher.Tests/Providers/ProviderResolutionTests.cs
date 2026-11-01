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
        string? apiKey = null) => new()
    {
        Name = name,
        BaseUri = new Uri(uri),
        DefaultProtocol = protocol,
        DefaultModel = defaultModel,
        ApiKey = apiKey
    };

    private static LauncherSettings SettingsWith(string defaultPointer, params Provider[] backends) => new()
    {
        Providers = [.. backends],
        DefaultProvider = defaultPointer
    };

    [Test]
    public async Task Resolve_selects_entry_named_by_provider_flag()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "secondary", null, null, null);

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_matches_provider_name_case_insensitively()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "SECONDARY", null, null, null);

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_endpoint_and_protocol_flags_override_only_those_fields()
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

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.BaseUri).IsEqualTo(new Uri("https://override.example/v1"));
        await Assert.That(resolved.Provider.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
        // Every other field comes from the saved entry - including the key,
        // which is never overridable per launch.
        await Assert.That(resolved.Provider.Name).IsEqualTo("secondary");
        await Assert.That(resolved.Provider.DefaultModel).IsEqualTo("gpt-4o");
        await Assert.That(resolved.Provider.ApiKey).IsEqualTo("hgkey:secondary");
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

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.DefaultModel).IsEqualTo("gpt-5");
        await Assert.That(resolved.Provider.BaseUri).IsEqualTo(new Uri("https://secondary.example/v1"));
        await Assert.That(resolved.Provider.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task Resolve_without_flags_reads_the_default_pointer_only()
    {
        LauncherSettings settings = SettingsWith("secondary", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_absent_default_with_entries_fails_without_first_entry_fallback()
    {
        LauncherSettings settings = SettingsWith(string.Empty, Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.AbsentDefaultProvider);
        await Assert.That(failed.Message).Contains("config set defaultBackend");
    }

    [Test]
    public async Task Resolve_dangling_default_pointer_fails_with_pointer_directive()
    {
        LauncherSettings settings = SettingsWith("ghost", Entry("primary"), Entry("secondary"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, null, null, null, null);

        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.DanglingDefaultProvider);
        await Assert.That(failed.Message).Contains("unknown backend name 'ghost'");
        await Assert.That(failed.Message).Contains("config set defaultBackend");
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
        await Assert.That(failed.Message).Contains("unknown backend name 'nope'");
    }

    [Test]
    public async Task Resolve_unknown_provider_name_may_hint_a_close_match_without_changing_the_outcome()
    {
        LauncherSettings settings = SettingsWith("production", Entry("production"));

        ProviderResolutionResult result = ProviderResolution.Resolve(settings, "prod", null, null, null);

        // The hint is best-effort, non-contractual UX; the outcome is the failure.
        ProviderResolutionResult.Failed failed = (ProviderResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(ProviderResolutionFailure.UnknownProviderName);
        await Assert.That(failed.Message).Contains("unknown backend name 'prod'");
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
    public async Task Resolve_raw_url_without_saved_entries_yields_an_override_provider()
    {
        LauncherSettings settings = SettingsWith(string.Empty);

        ProviderResolutionResult result = ProviderResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "anthropic",
            "claude-sonnet-5");

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.Name).IsEqualTo("override");
        await Assert.That(resolved.Provider.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(resolved.Provider.DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(resolved.Provider.DefaultModel).IsEqualTo("claude-sonnet-5");
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

        ProviderResolutionResult.Resolved resolved = (ProviderResolutionResult.Resolved)result;
        await Assert.That(resolved.Provider.Name).IsEqualTo("primary");
        await Assert.That(resolved.Provider.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(resolved.Provider.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiResponses);
        await Assert.That(resolved.Provider.ApiKey).IsEqualTo("hgkey:primary");
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
        await Assert.That(failed.Message).Contains("config set defaultBackend");
    }
}
