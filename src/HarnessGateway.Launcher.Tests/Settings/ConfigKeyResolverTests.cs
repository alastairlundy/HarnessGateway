using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Settings;

public class ConfigKeyResolverTests
{
    private static LauncherSettings SettingsWith(
        string defaultBackend = "default",
        string? apiKey = "hgkey:default",
        string? defaultModel = "gpt-4") =>
        new()
        {
            Providers =
            [
                new Provider
                {
                    Name = "default",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    ApiKey = apiKey,
                    DefaultModel = defaultModel
                },
                new Provider
                {
                    Name = "work",
                    BaseUri = new Uri("https://api.work.example/v1"),
                    DefaultProtocol = ProtocolKind.Anthropic
                }
            ],
            DefaultProvider = defaultBackend
        };

    [Test]
    public async Task Get_defaultBackend_returns_the_pointer()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(), "defaultBackend");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo("default");
    }

    [Test]
    public async Task Get_schemaVersion_returns_the_stamped_version()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(), "schemaVersion");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(LauncherSettings.CurrentSchemaVersion.ToString());
    }

    [Test]
    public async Task Get_providers_returns_names_only_never_endpoint_protocol_or_key_values()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Get(settings, "backends");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).Contains("default");
        await Assert.That(result.Value).Contains("work");
        await Assert.That(result.Value!.Contains("https://")).IsFalse();
        await Assert.That(result.Value.Contains("openai")).IsFalse();
        await Assert.That(result.Value.Contains("anthropic")).IsFalse();
        await Assert.That(result.Value.Contains("hgkey")).IsFalse();
    }

    [Test]
    public async Task Get_provider_fields_round_trip_each_shape()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult endpoint = ConfigKeyResolver.Get(settings, "backends.default.endpoint");
        ConfigKeyResult protocol = ConfigKeyResolver.Get(settings, "backends.default.protocol");
        ConfigKeyResult model = ConfigKeyResolver.Get(settings, "backends.default.defaultModel");

        await Assert.That(endpoint.Value).IsEqualTo("https://api.example.com/v1");
        await Assert.That(protocol.Value).IsEqualTo("openai-chat");
        await Assert.That(model.Value).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task Get_provider_field_with_unset_model_reads_empty_not_null()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(defaultModel: null), "backends.default.defaultModel");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Get_unknown_key_is_a_usage_error()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(), "backends.default.apiKey");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnknownKey);
    }

    [Test]
    public async Task Get_unaddressable_provider_key_is_a_usage_error()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(), "backends.nope.endpoint");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnaddressableKey);
    }

    [Test]
    public async Task Get_malformed_dotted_key_is_unknown()
    {
        ConfigKeyResult result = ConfigKeyResolver.Get(SettingsWith(), "backends.default");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnknownKey);
    }

    [Test]
    public async Task Set_defaultBackend_persists_the_canonical_entry_name()
    {
        LauncherSettings settings = SettingsWith(defaultBackend: string.Empty);

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "defaultBackend", "WORK");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.AlreadySatisfied).IsFalse();
        // The pointer stores the canonical entry name, not the typed casing.
        await Assert.That(result.Settings!.DefaultProvider).IsEqualTo("work");
        await Assert.That(result.Message).Contains("'work'");
    }

    [Test]
    public async Task Set_defaultBackend_already_default_is_idempotent()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "defaultBackend", "default");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.AlreadySatisfied).IsTrue();
        await Assert.That(result.Settings!.DefaultProvider).IsEqualTo("default");
    }

    [Test]
    public async Task Set_defaultBackend_unknown_name_is_the_exit_8_failure_class()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "defaultBackend", "nope");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnknownProviderName);
        // The contract message from backend resolution, not a bespoke string.
        await Assert.That(result.Message).Contains("unknown provider name 'nope'");
    }

    [Test]
    public async Task Set_schemaVersion_is_read_only()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "schemaVersion", "99");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.ReadOnlyKey);
    }

    [Test]
    public async Task Set_providers_is_read_only()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends", "default");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.ReadOnlyKey);
    }

    [Test]
    public async Task Set_provider_endpoint_persists_and_leaves_other_fields_untouched()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(
            settings, "backends.work.endpoint", "https://new.example.com/v1");

        await Assert.That(result.IsSuccess).IsTrue();
        Provider updated = result.Settings!.Providers.Single(b => b.Name == "work");
        await Assert.That(updated.BaseUri).IsEqualTo(new Uri("https://new.example.com/v1"));
        await Assert.That(updated.DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(updated.DefaultModel).IsNull();
        await Assert.That(updated.ApiKey).IsNull();
        // The sibling default entry is untouched.
        await Assert.That(result.Settings.Providers[0].BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task Set_provider_protocol_persists()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.protocol", "anthropic");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Settings!.Providers[0].DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(result.Settings.Providers[0].BaseUri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task Set_provider_defaultModel_persists()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.defaultModel", "claude-sonnet-5");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Settings!.Providers[0].DefaultModel).IsEqualTo("claude-sonnet-5");
    }

    [Test]
    public async Task Set_invalid_endpoint_is_an_invalid_value()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.endpoint", "not-a-url");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.InvalidValue);
    }

    [Test]
    public async Task Set_invalid_protocol_is_an_invalid_value()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.protocol", "grpc");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.InvalidValue);
    }

    [Test]
    public async Task Set_unknown_key_is_a_usage_error()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.apiKey", "sk-secret");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnknownKey);
        // The hard boundary: no config set key can write secret material.
        await Assert.That(settings.Providers[0].ApiKey).IsEqualTo("hgkey:default");
    }

    [Test]
    public async Task Set_unaddressable_provider_key_is_a_usage_error()
    {
        LauncherSettings settings = SettingsWith();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.nope.endpoint", "https://x.example/v1");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ConfigKeyFailure.UnaddressableKey);
        await Assert.That(settings.Providers).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Set_provider_field_never_mutates_on_failure()
    {
        LauncherSettings settings = SettingsWith();
        string before = settings.Providers[0].BaseUri.ToString();

        ConfigKeyResult result = ConfigKeyResolver.Set(settings, "backends.default.endpoint", "not-a-url");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(settings.Providers[0].BaseUri.ToString()).IsEqualTo(before);
    }
}
