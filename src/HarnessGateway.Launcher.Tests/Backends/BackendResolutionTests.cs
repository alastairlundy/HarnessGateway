using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Backends;

public class BackendResolutionTests
{
    private static Backend Entry(
        string name,
        string uri = "https://api.example.com/v1",
        ProtocolKind protocol = ProtocolKind.OpenAiChat,
        string? defaultModel = null,
        string? apiKey = null) => new()
    {
        Name = name,
        BaseUri = new Uri(uri),
        Protocol = protocol,
        DefaultModel = defaultModel,
        ApiKey = apiKey
    };

    private static LauncherSettings SettingsWith(string defaultPointer, params Backend[] backends) => new()
    {
        Backends = [.. backends],
        DefaultBackend = defaultPointer
    };

    [Test]
    public async Task Resolve_selects_entry_named_by_backend_flag()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, "secondary", null, null, null);

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_matches_backend_name_case_insensitively()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"), Entry("secondary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, "SECONDARY", null, null, null);

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.Name).IsEqualTo("secondary");
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

        BackendResolutionResult result = BackendResolution.Resolve(
            settings,
            "secondary",
            "https://override.example/v1",
            "openai-chat",
            null);

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.BaseUri).IsEqualTo(new Uri("https://override.example/v1"));
        await Assert.That(resolved.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        // Every other field comes from the saved entry - including the key,
        // which is never overridable per launch.
        await Assert.That(resolved.Backend.Name).IsEqualTo("secondary");
        await Assert.That(resolved.Backend.DefaultModel).IsEqualTo("gpt-4o");
        await Assert.That(resolved.Backend.ApiKey).IsEqualTo("hgkey:secondary");
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

        BackendResolutionResult result = BackendResolution.Resolve(settings, "secondary", null, null, "gpt-5");

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.DefaultModel).IsEqualTo("gpt-5");
        await Assert.That(resolved.Backend.BaseUri).IsEqualTo(new Uri("https://secondary.example/v1"));
        await Assert.That(resolved.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task Resolve_without_flags_reads_the_default_pointer_only()
    {
        LauncherSettings settings = SettingsWith("secondary", Entry("primary"), Entry("secondary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, null, null, null);

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.Name).IsEqualTo("secondary");
    }

    [Test]
    public async Task Resolve_absent_default_with_entries_fails_without_first_entry_fallback()
    {
        LauncherSettings settings = SettingsWith(string.Empty, Entry("primary"), Entry("secondary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, null, null, null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.AbsentDefaultBackend);
        await Assert.That(failed.Message).Contains("config set defaultBackend");
    }

    [Test]
    public async Task Resolve_dangling_default_pointer_fails_with_pointer_directive()
    {
        LauncherSettings settings = SettingsWith("ghost", Entry("primary"), Entry("secondary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, null, null, null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.DanglingDefaultBackend);
        await Assert.That(failed.Message).Contains("unknown backend name 'ghost'");
        await Assert.That(failed.Message).Contains("config set defaultBackend");
    }

    [Test]
    public async Task Resolve_without_entries_and_without_pointer_returns_no_backend()
    {
        LauncherSettings settings = SettingsWith(string.Empty);

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, null, null, null);

        await Assert.That(result).IsTypeOf<BackendResolutionResult.EmptySettings>();
    }

    [Test]
    public async Task Resolve_unknown_backend_name_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, "nope", null, null, null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.UnknownBackendName);
        await Assert.That(failed.Message).Contains("unknown backend name 'nope'");
    }

    [Test]
    public async Task Resolve_unknown_backend_name_may_hint_a_close_match_without_changing_the_outcome()
    {
        LauncherSettings settings = SettingsWith("production", Entry("production"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, "prod", null, null, null);

        // The hint is best-effort, non-contractual UX; the outcome is the failure.
        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.UnknownBackendName);
        await Assert.That(failed.Message).Contains("unknown backend name 'prod'");
        await Assert.That(failed.Message).Contains("production");
    }

    [Test]
    public async Task Resolve_endpoint_without_protocol_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, "https://api.example.com/v1", null, null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.EndpointWithoutProtocol);
        await Assert.That(failed.Message).Contains("--endpoint requires --protocol");
    }

    [Test]
    public async Task Resolve_protocol_without_endpoint_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, null, "openai-chat", null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.ProtocolWithoutEndpoint);
        await Assert.That(failed.Message).Contains("--protocol requires --endpoint");
    }

    [Test]
    public async Task Resolve_invalid_endpoint_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(settings, null, "not-a-url", "openai-chat", null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.InvalidEndpoint);
        await Assert.That(failed.Message).Contains("not-a-url");
    }

    [Test]
    public async Task Resolve_invalid_protocol_fails()
    {
        LauncherSettings settings = SettingsWith("primary", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(
            settings,
            null,
            "https://api.example.com/v1",
            "smtp",
            null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.InvalidProtocol);
        await Assert.That(failed.Message).Contains("openai-chat, openai-responses, anthropic");
    }

    [Test]
    public async Task Resolve_raw_url_without_saved_entries_yields_an_override_backend()
    {
        LauncherSettings settings = SettingsWith(string.Empty);

        BackendResolutionResult result = BackendResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "anthropic",
            "claude-sonnet-5");

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.Name).IsEqualTo("override");
        await Assert.That(resolved.Backend.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(resolved.Backend.Protocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(resolved.Backend.DefaultModel).IsEqualTo("claude-sonnet-5");
    }

    [Test]
    public async Task Resolve_raw_url_layers_onto_the_resolved_entry_and_keeps_its_key()
    {
        LauncherSettings settings = SettingsWith(
            "primary",
            Entry("primary", uri: "https://primary.example/v1", apiKey: "hgkey:primary"));

        BackendResolutionResult result = BackendResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "openai-responses",
            null);

        BackendResolutionResult.Resolved resolved = (BackendResolutionResult.Resolved)result;
        await Assert.That(resolved.Backend.Name).IsEqualTo("primary");
        await Assert.That(resolved.Backend.BaseUri).IsEqualTo(new Uri("https://raw.example/v1"));
        await Assert.That(resolved.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
        await Assert.That(resolved.Backend.ApiKey).IsEqualTo("hgkey:primary");
    }

    [Test]
    public async Task Resolve_raw_url_still_reads_the_default_pointer_strictly()
    {
        // A fully explicit raw URL does not bypass a broken profile: the
        // unresolvable default pointer is still the code-8 failure class.
        LauncherSettings settings = SettingsWith("ghost", Entry("primary"));

        BackendResolutionResult result = BackendResolution.Resolve(
            settings,
            null,
            "https://raw.example/v1",
            "openai-chat",
            null);

        BackendResolutionResult.Failed failed = (BackendResolutionResult.Failed)result;
        await Assert.That(failed.Failure).IsEqualTo(BackendResolutionFailure.DanglingDefaultBackend);
        await Assert.That(failed.Message).Contains("config set defaultBackend");
    }
}
