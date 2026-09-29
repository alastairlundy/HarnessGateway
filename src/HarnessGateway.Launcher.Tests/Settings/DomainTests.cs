using HarnessGateway.Launcher.Core.Backends;
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

public class BackendTests
{
    [Test]
    public async Task Backend_record_stores_all_fields()
    {
        Backend backend = new Backend
        {
            Name = "my-api",
            BaseUri = new Uri("https://api.openai.com/v1"),
            Protocol = ProtocolKind.OpenAiChat,
            DefaultModel = "gpt-4",
            ApiKey = "hgkey:my-api"
        };

        await Assert.That(backend.Name).IsEqualTo("my-api");
        await Assert.That(backend.BaseUri).IsEqualTo(new Uri("https://api.openai.com/v1"));
        await Assert.That(backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(backend.DefaultModel).IsEqualTo("gpt-4");
        await Assert.That(backend.ApiKey).IsEqualTo("hgkey:my-api");
    }

    [Test]
    public async Task Backend_allows_null_optional_fields()
    {
        Backend backend = new Backend
        {
            Name = "minimal",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = ProtocolKind.Anthropic
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

        await Assert.That(settings.Backends).IsEmpty();
        await Assert.That(settings.DefaultBackend).IsEmpty();
        await Assert.That(settings.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);
    }

    [Test]
    public async Task LauncherSettings_tolerates_multiple_backends_without_behavior_change()
    {
        LauncherSettings settings = new LauncherSettings
        {
            Backends =
            [
                new Backend { Name = "first", BaseUri = new Uri("https://a.com"), Protocol = ProtocolKind.OpenAiChat },
                new Backend { Name = "second", BaseUri = new Uri("https://b.com"), Protocol = ProtocolKind.Anthropic }
            ],
            DefaultBackend = "first",
            SchemaVersion = 1
        };

        await Assert.That(settings.Backends).Count().IsEqualTo(2);
        await Assert.That(settings.DefaultBackend).IsEqualTo("first");
    }
}
