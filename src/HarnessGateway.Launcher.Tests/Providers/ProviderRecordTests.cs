using System.Text.Json;
using HarnessGateway.Launcher.Core.Providers;

namespace HarnessGateway.Launcher.Tests.Providers;

public class ProviderRecordTests
{
    [Test]
    public async Task Provider_defaults_are_an_empty_model_list_and_no_optional_fields()
    {
        Provider provider = new()
        {
            Name = "openai",
            BaseUri = new Uri("https://api.openai.com/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
        };

        await Assert.That(provider.Models).IsEmpty();
        await Assert.That(provider.DefaultModel).IsNull();
        await Assert.That(provider.ApiKey).IsNull();
    }

    [Test]
    public async Task Provider_serializes_one_default_protocol_and_a_models_array()
    {
        Provider provider = new()
        {
            Name = "work",
            BaseUri = new Uri("https://api.work.example/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            DefaultModel = "gpt-4o",
            ApiKey = "hgkey:work",
            Models =
            [
                new ModelEntry { Name = "gpt-4o" },
                new ModelEntry { Name = "o3", Protocol = ProtocolKind.OpenAiResponses },
            ],
        };

        string json = JsonSerializer.Serialize(provider);

        await Assert.That(json).Contains("\"protocol\"");
        await Assert.That(json).Contains("\"models\"");
        await Assert.That(json).Contains("\"defaultModel\"");
        await Assert.That(json).Contains("\"apiKey\"");
    }

    [Test]
    public async Task Provider_round_trips_through_the_models_array_shape()
    {
        Provider provider = new()
        {
            Name = "work",
            BaseUri = new Uri("https://api.work.example/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            DefaultModel = "gpt-4o",
            ApiKey = "hgkey:work",
            Models =
            [
                new ModelEntry { Name = "gpt-4o" },
                new ModelEntry { Name = "o3", Protocol = ProtocolKind.OpenAiResponses },
            ],
        };

        Provider? roundTripped = JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(provider));

        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped.Name).IsEqualTo("work");
        await Assert.That(roundTripped.BaseUri).IsEqualTo(new Uri("https://api.work.example/v1"));
        await Assert.That(roundTripped.DefaultProtocol).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(roundTripped.DefaultModel).IsEqualTo("gpt-4o");
        await Assert.That(roundTripped.ApiKey).IsEqualTo("hgkey:work");
        await Assert.That(roundTripped.Models.Count).IsEqualTo(2);
        await Assert.That(roundTripped.Models[0].Name).IsEqualTo("gpt-4o");
        await Assert.That(roundTripped.Models[0].Protocol).IsNull();
        await Assert.That(roundTripped.Models[1].Name).IsEqualTo("o3");
        await Assert.That(roundTripped.Models[1].Protocol).IsEqualTo(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task ModelEntry_protocol_null_means_the_provider_default_applies()
    {
        ModelEntry unmarked = new() { Name = "gpt-4o" };
        ModelEntry overridden = new() { Name = "o3", Protocol = ProtocolKind.Anthropic };

        await Assert.That(unmarked.Protocol).IsNull();
        await Assert.That(overridden.Protocol).IsEqualTo(ProtocolKind.Anthropic);
    }
}
