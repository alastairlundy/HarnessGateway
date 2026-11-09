using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Tests.Settings;

public class ProtocolKindTests
{
    [Test]
    public async Task ProtocolKind_maps_to_flag_values()
    {
        await Assert.That(ProtocolKind.OpenAiChat.ToString()).IsEqualTo("OpenAiChat");
        await Assert.That(ProtocolKind.OpenAiResponses.ToString()).IsEqualTo("OpenAiResponses");
        await Assert.That(ProtocolKind.Anthropic.ToString()).IsEqualTo("Anthropic");
    }
}

public class ProviderTests
{
    [Test]
    public async Task Backend_record_stores_all_fields()
    {
        Provider backend = new Provider
        {
            Name = "my-api",
            BaseUri = new Uri("https://api.openai.com/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            DefaultModel = "gpt-4",
            ApiKey = "hgkey:my-api"
        };

        await Assert.That(backend.Name).IsEqualTo("my-api");
        await Assert.That(backend.BaseUri).IsEqualTo(new Uri("https://api.openai.com/v1"));
        await Assert.That(backend.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(backend.DefaultModel).IsEqualTo("gpt-4");
        await Assert.That(backend.ApiKey).IsEqualTo("hgkey:my-api");
    }

    [Test]
    public async Task Backend_allows_null_optional_fields()
    {
        Provider backend = new Provider
        {
            Name = "minimal",
            BaseUri = new Uri("https://api.example.com"),
            DefaultProtocol = ProtocolKind.Anthropic
        };

        await Assert.That(backend.DefaultModel).IsNull();
        await Assert.That(backend.ApiKey).IsNull();
    }
}

public class LauncherSettingsTests
{
    [Test]
    public async Task LauncherSettings_defaults_are_valid()
    {
        LauncherSettings settings = new LauncherSettings();

        await Assert.That(settings.Providers).IsEmpty();
        await Assert.That(settings.DefaultProvider).IsEmpty();
        await Assert.That(settings.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
    }

    [Test]
    public async Task LauncherSettings_tolerates_multiple_providers_without_behavior_change()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Providers =
            [
                new Provider { Name = "first", BaseUri = new Uri("https://a.com"), DefaultProtocol = ProtocolKind.OpenAiChat },
                new Provider { Name = "second", BaseUri = new Uri("https://b.com"), DefaultProtocol = ProtocolKind.Anthropic }
            ],
            DefaultProvider = "first",
            SchemaVersion = 1
        };

        await Assert.That(settings.Providers).Count().IsEqualTo(2);
        await Assert.That(settings.DefaultProvider).IsEqualTo("first");
    }
}
