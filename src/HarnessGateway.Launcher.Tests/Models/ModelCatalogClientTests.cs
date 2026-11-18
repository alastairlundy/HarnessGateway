using System.Net;
using System.Text.Json;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;

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

        Provider backend = new Provider
        {
            Name = "openai",
            BaseUri = new Uri("https://api.openai.com"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            ApiKey = "sk-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(2);
        await Assert.That(catalog.Models[0].Id).IsEqualTo("gpt-4");
        await Assert.That(catalog.Models[1].Id).IsEqualTo("gpt-3.5-turbo");
        await Assert.That(catalog.FetchedAt).IsNotEqualTo(default(DateTimeOffset));
        await Assert.That(handler.LastRequest?.RequestUri?.AbsoluteUri).IsEqualTo("https://api.openai.com/v1/models");
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

        Provider backend = new Provider
        {
            Name = "anthropic",
            BaseUri = new Uri("https://api.anthropic.com"),
            DefaultProtocol = ProtocolKind.Anthropic,
            ApiKey = "sk-ant-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(2);
        await Assert.That(catalog.Models[0].Id).IsEqualTo("claude-sonnet-5");
        await Assert.That(catalog.Models[1].Id).IsEqualTo("claude-haiku-4");
        await Assert.That(handler.LastRequest?.RequestUri?.AbsoluteUri).IsEqualTo("https://api.anthropic.com/v1/models");
    }

    [Test]
    public async Task Unreachable_endpoint_propagates_the_failure_to_the_wizard()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler(null, isUnreachable: true);
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://unreachable.example.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "unreachable",
            BaseUri = new Uri("https://unreachable.example.com"),
            DefaultProtocol = ProtocolKind.OpenAiChat
        };

        // ModelStep catches this, reports it, and degrades to free-text entry.
        await Assert.That(async () => await catalogClient.FetchModelsAsync(backend))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task Malformed_response_propagates_the_failure_to_the_wizard()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler("not valid json");
        HttpClient client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "malformed",
            BaseUri = new Uri("https://api.example.com"),
            DefaultProtocol = ProtocolKind.OpenAiChat
        };

        await Assert.That(async () => await catalogClient.FetchModelsAsync(backend))
            .Throws<JsonException>();
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

        Provider backend = new Provider
        {
            Name = "openai-responses",
            BaseUri = new Uri("https://api.openai.com"),
            DefaultProtocol = ProtocolKind.OpenAiResponses,
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

        Provider backend = new Provider
        {
            Name = "local",
            BaseUri = new Uri("https://localhost:11434"),
            DefaultProtocol = ProtocolKind.OpenAiChat
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(handler.LastRequest?.Headers.Authorization).IsNull();
    }

    [Test]
    public async Task OpenAi_request_targets_configured_base_uri_without_client_base_address()
    {
        var openAiResponse = new
        {
            data = new[]
            {
                new { id = "gpt-4", @object = "model" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(openAiResponse));
        HttpClient client = new HttpClient(handler);
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "openai",
            BaseUri = new Uri("https://api.openai.example"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            ApiKey = "sk-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        HttpRequestMessage? lastRequest = handler.LastRequest;
        await Assert.That(lastRequest).IsNotNull();
        HttpRequestMessage request = lastRequest!;

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        Uri requestUri = request.RequestUri!;
        await Assert.That(requestUri.IsAbsoluteUri).IsTrue();
        await Assert.That(requestUri.Host).IsEqualTo("api.openai.example");
        await Assert.That(requestUri.AbsolutePath).IsEqualTo("/v1/models");
        await Assert.That(request.Headers.Authorization?.ToString()).IsEqualTo("Bearer sk-test");
    }

    [Test]
    public async Task Anthropic_request_targets_configured_base_uri_and_sends_x_api_key()
    {
        var anthropicResponse = new
        {
            data = new[]
            {
                new { id = "claude-sonnet-5", display_name = "Claude Sonnet 5" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(anthropicResponse));
        HttpClient client = new HttpClient(handler);
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "anthropic",
            BaseUri = new Uri("https://api.anthropic.example"),
            DefaultProtocol = ProtocolKind.Anthropic,
            ApiKey = "sk-ant-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        HttpRequestMessage? lastRequest = handler.LastRequest;
        await Assert.That(lastRequest).IsNotNull();
        HttpRequestMessage request = lastRequest!;

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        Uri requestUri = request.RequestUri!;
        await Assert.That(requestUri.IsAbsoluteUri).IsTrue();
        await Assert.That(requestUri.Host).IsEqualTo("api.anthropic.example");
        await Assert.That(requestUri.AbsolutePath).IsEqualTo("/v1/models");
        await Assert.That(request.Headers.Contains("x-api-key")).IsTrue();
        await Assert.That(request.Headers.GetValues("x-api-key").Single()).IsEqualTo("sk-ant-test");
    }

    [Test]
    public async Task Base_uri_ending_in_v1_does_not_double_the_v1_segment()
    {
        var openAiResponse = new
        {
            data = new[]
            {
                new { id = "gpt-4o", @object = "model" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(openAiResponse));
        HttpClient client = new HttpClient(handler);
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "gateway",
            BaseUri = new Uri("https://gateway.example.com/v1"),
            DefaultProtocol = ProtocolKind.OpenAiChat,
            ApiKey = "sk-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        HttpRequestMessage? lastRequest = handler.LastRequest;
        await Assert.That(lastRequest).IsNotNull();

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(lastRequest!.RequestUri?.AbsoluteUri).IsEqualTo("https://gateway.example.com/v1/models");
    }

    [Test]
    public async Task Anthropic_request_omits_anthropic_version_header()
    {
        var anthropicResponse = new
        {
            data = new[]
            {
                new { id = "claude-sonnet-5", display_name = "Claude Sonnet 5" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(anthropicResponse));
        HttpClient client = new HttpClient(handler);
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "anthropic",
            BaseUri = new Uri("https://api.anthropic.example"),
            DefaultProtocol = ProtocolKind.Anthropic,
            ApiKey = "sk-ant-test"
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        HttpRequestMessage? lastRequest = handler.LastRequest;
        await Assert.That(lastRequest).IsNotNull();
        HttpRequestMessage request = lastRequest!;

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(request.Headers.Contains("anthropic-version")).IsFalse();
        await Assert.That(request.Headers.Contains("x-api-key")).IsTrue();
    }

    [Test]
    public async Task Anthropic_backend_without_api_key_omits_x_api_key_header()
    {
        var anthropicResponse = new
        {
            data = new[]
            {
                new { id = "claude-sonnet-5", display_name = "Claude Sonnet 5" }
            }
        };

        StubHttpMessageHandler handler = new StubHttpMessageHandler(JsonSerializer.Serialize(anthropicResponse));
        HttpClient client = new HttpClient(handler);
        ModelCatalogClient catalogClient = new ModelCatalogClient(client);

        Provider backend = new Provider
        {
            Name = "anthropic-local",
            BaseUri = new Uri("https://localhost:9443"),
            DefaultProtocol = ProtocolKind.Anthropic
        };

        ModelCatalog catalog = await catalogClient.FetchModelsAsync(backend);

        HttpRequestMessage? lastRequest = handler.LastRequest;
        await Assert.That(lastRequest).IsNotNull();
        HttpRequestMessage request = lastRequest!;

        await Assert.That(catalog.Models).Count().IsEqualTo(1);
        await Assert.That(request.Headers.Contains("x-api-key")).IsFalse();
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
