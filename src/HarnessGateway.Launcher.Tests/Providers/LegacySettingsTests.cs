using System.Text.Json;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Serialization;
using HarnessGateway.Launcher.Core.Settings;

// The legacy read model is deliberately obsolete (removed in the first
// release after v1); this file is its test surface, so every reference to
// LegacySettings/LegacyBackend here is intentional.
#pragma warning disable CS0618

namespace HarnessGateway.Launcher.Tests.Providers;

public class LegacySettingsTests
{
    private const string RawV2Json =
        """
        {
          "backends": [
            {
              "name": "work",
              "baseUri": "https://api.work.example/v1",
              "protocol": 0,
              "defaultModel": "gpt-4",
              "apiKey": "hgkey:work"
            },
            {
              "name": "anthropic-alias",
              "baseUri": "https://anthropic.example/v1",
              "protocol": 2
            }
          ],
          "defaultBackend": "work",
          "schemaVersion": 2
        }
        """;

    [Test]
    public async Task V2_file_shape_converts_to_providers_with_the_mapping_rules()
    {
        LegacySettings? legacy = JsonSerializer.Deserialize(
            RawV2Json,
            SettingsJsonContext.Default.LegacySettings);

        await Assert.That(legacy).IsNotNull();

        LauncherSettings converted = LegacySettings.ToLauncherSettings(legacy!);

        // defaultBackend maps to defaultProvider verbatim (T020).
        await Assert.That(converted.DefaultProvider).IsEqualTo("work");
        await Assert.That(converted.SchemaVersion).IsEqualTo(LauncherSettings.CurrentSchemaVersion);

        // One Provider per legacy backend, in declaration order.
        await Assert.That(converted.Providers).Count().IsEqualTo(2);

        Provider first = converted.Providers[0];
        await Assert.That(first.Name).IsEqualTo("work");
        await Assert.That(first.BaseUri).IsEqualTo(new Uri("https://api.work.example/v1"));
        await Assert.That(first.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(first.DefaultModel).IsEqualTo("gpt-4");
        await Assert.That(first.ApiKey).IsEqualTo("hgkey:work");
        await Assert.That(first.Models).IsEmpty();

        Provider second = converted.Providers[1];
        await Assert.That(second.Name).IsEqualTo("anthropic-alias");
        await Assert.That(second.DefaultProtocol).IsEqualTo(ProtocolKind.Anthropic);
        await Assert.That(second.DefaultModel).IsNull();
        await Assert.That(second.Models).IsEmpty();
    }

    [Test]
    public async Task Convert_keeps_a_dangling_default_pointer_dangling()
    {
        LegacySettings legacy = new()
        {
            Backends =
            [
                new LegacyBackend
                {
                    Name = "work",
                    BaseUri = new Uri("https://api.work.example/v1"),
                    Protocol = ProtocolKind.OpenAiChat,
                }
            ],
            DefaultBackend = "ghost",
            SchemaVersion = 2,
        };

        LauncherSettings converted = LegacySettings.ToLauncherSettings(legacy);

        // No healing here: the pointer stays dangling and fails later at the
        // strict-pointer read (T020).
        await Assert.That(converted.DefaultProvider).IsEqualTo("ghost");
        await Assert.That(converted.Providers.Select(p => p.Name)).DoesNotContain("ghost");
    }

    [Test]
    public async Task Legacy_read_model_types_carry_obsolescence_markers_pointing_at_post_v1_removal()
    {
        ObsoleteAttribute? settingsMarker = (ObsoleteAttribute?)Attribute.GetCustomAttribute(
            typeof(LegacySettings), typeof(ObsoleteAttribute));
        ObsoleteAttribute? backendMarker = (ObsoleteAttribute?)Attribute.GetCustomAttribute(
            typeof(LegacyBackend), typeof(ObsoleteAttribute));

        await Assert.That(settingsMarker).IsNotNull();
        await Assert.That(backendMarker).IsNotNull();
        await Assert.That(settingsMarker!.Message).Contains("first release after v1");
        await Assert.That(backendMarker!.Message).Contains("first release after v1");
    }
}
