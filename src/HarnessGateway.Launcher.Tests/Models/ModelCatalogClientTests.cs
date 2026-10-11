using System.Net;
using System.Text.Json;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Backends.Models;

namespace HarnessGateway.Launcher.Tests.Models;

public class ModelCatalogClientTests
{
    [Test]
    public async Task OpenAi_models_endpoint_parses_response()
    {
        var openAiResponse = new
        {
            data = new[]
            {
                new { id = "gpt-4", @object = "model" },
                new { id = "gpt-3.5-turbo", @object = "model" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(openAiResponse));
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "openai",
            BaseUri = new Uri("https://api.openai.com"),
            Protocol = ProtocolKind.OpenAiChat,
            ApiKey = "sk-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(2);
        await Assert.That(catalog.Models[0].Id).IsEqualTo("gpt-4");
        await Assert.That(catalog.Models[1].Id).IsEqualTo("gpt-3.5-turbo");
        await Assert.That(catalog.FetchedAt).IsNotEqualTo(default(DateTimeOffset));
    }

    [Test]
    public async Task Anthropic_models_endpoint_parses_response()
    {
        var anthropicResponse = new
        {
            data = new[]
            {
                new { id = "claude-sonnet-5", display_name = "Claude Sonnet 5" },
                new { id = "claude-haiku-4", display_name = "Claude Haiku 4" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(anthropicResponse));
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "anthropic",
            BaseUri = new Uri("https://api.anthropic.com"),
            Protocol = ProtocolKind.Anthropic,
            ApiKey = "sk-ant-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(2);
        await Assert.That(catalog.Models[0].Id).IsEqualTo("claude-sonnet-5");
        await Assert.That(catalog.Models[1].Id).IsEqualTo("claude-haiku-4");
    }

    [Test]
    public async Task Unreachable_endpoint_returns_graceful_empty_catalog()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler(null, isUnreachable: true);
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://unreachable.example.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "unreachable",
            BaseUri = new Uri("https://unreachable.example.com"),
            Protocol = ProtocolKind.OpenAiChat
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).IsEmpty();
        await Assert.That(catalog.FetchedAt).IsNotEqualTo(default(DateTimeOffset));
    }

    [Test]
    public async Task Malformed_response_returns_graceful_empty_catalog()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler("not valid json");
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "malformed",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = ProtocolKind.OpenAiChat
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).IsEmpty();
    }

    [Test]
    public async Task OpenAi_responses_protocol_uses_same_endpoint()
    {
        var openAiResponse = new
        {
            data = new[]
            {
                new { id = "gpt-4o", @object = "model" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(openAiResponse));
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "openai-responses",
            BaseUri = new Uri("https://api.openai.com"),
            Protocol = ProtocolKind.OpenAiResponses,
            ApiKey = "sk-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(catalog.Models[0].Id).IsEqualTo("gpt-4o");
    }

    [Test]
    public async Task Backend_without_api_key_omits_authorization_header()
    {
        var openAiResponse = new
        {
            data = new[]
            {
                new { id = "model-1", @object = "model" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(openAiResponse));
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:11434") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Backend backend = new Backend
        {
            Name = "local",
            BaseUri = new Uri("https://localhost:11434"),
            Protocol = ProtocolKind.OpenAiChat
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(handler.LastRequest?.Headers.Authorization).IsNull();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _response;
        private readonly bool _isUnreachable;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHttpMessageHandler(string? response, bool isUnreachable = false)
        {
            _response = response;
            _isUnreachable = isUnreachable;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (_isUnreachable)
            {
                throw new HttpRequestException("Connection refused");
            }

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response ?? "{}", System.Text.Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
