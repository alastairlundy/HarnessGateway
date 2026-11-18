using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Providers.Models;

public sealed class ModelCatalogClient
{
    private readonly HttpClient _httpClient;

    public ModelCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ModelCatalog> FetchModelsAsync(Provider backend, CancellationToken cancellationToken = default)
    {
        // Failures deliberately propagate: the wizard's ModelStep catches them,
        // reports "Could not fetch model catalogue", and degrades to free-text
        // entry. Swallowing them here hid the failure from the user entirely.
        return backend.DefaultProtocol switch
        {
            ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => await FetchOpenAiModelsAsync(backend, cancellationToken).ConfigureAwait(false),
            ProtocolKind.Anthropic => await FetchAnthropicModelsAsync(backend, cancellationToken).ConfigureAwait(false),
            _ => new([], DateTimeOffset.UtcNow),
        };
    }

    /// <summary>
    /// Builds the absolute <c>GET /v1/models</c> request URI from the provider's
    /// own endpoint so the request never depends on <see cref="HttpClient.BaseAddress"/>.
    /// When <see cref="Provider.BaseUri"/> already ends in a <c>/v1</c> segment only
    /// <c>/models</c> is appended, so no doubled <c>/v1</c> is produced.
    /// </summary>
    private static Uri BuildModelsUri(Provider backend)
    {
        Uri authority = new Uri(backend.BaseUri.GetLeftPart(UriPartial.Authority));

        string basePath = backend.BaseUri.AbsolutePath.TrimEnd('/');
        string modelsPath = basePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? basePath + "/models"
            : "/v1/models";

        return new Uri(authority, modelsPath);
    }

    private async Task<ModelCatalog> FetchOpenAiModelsAsync(Provider backend, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, BuildModelsUri(backend));

        if (!string.IsNullOrEmpty(backend.ApiKey))
        {
            request.Headers.Authorization = new("Bearer", backend.ApiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        OpenAiModelsResponse? openAiResponse =
            await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(
                    ModelCatalogJsonContext.Default.OpenAiModelsResponse,
                    cancellationToken)
                .ConfigureAwait(false);

        List<ModelCatalogEntry> models = openAiResponse?.Data?.Select(m => new ModelCatalogEntry(m.Id)).ToList() ?? [];

        return new(models, DateTimeOffset.UtcNow);
    }

    private async Task<ModelCatalog> FetchAnthropicModelsAsync(Provider backend, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, BuildModelsUri(backend));

        if (!string.IsNullOrEmpty(backend.ApiKey))
        {
            request.Headers.Add("x-api-key", backend.ApiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        AnthropicModelsResponse? anthropicResponse =
            await response.Content.ReadFromJsonAsync<AnthropicModelsResponse>(
                    ModelCatalogJsonContext.Default.AnthropicModelsResponse,
                    cancellationToken)
                .ConfigureAwait(false);

        List<ModelCatalogEntry> models = anthropicResponse?.Data?.Select(m => new ModelCatalogEntry(m.Id)).ToList() ?? [];

        return new(models, DateTimeOffset.UtcNow);
    }

    internal sealed class OpenAiModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiModel>? Data { get; set; }
    }

    internal sealed class OpenAiModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }

    internal sealed class AnthropicModelsResponse
    {
        [JsonPropertyName("data")]
        public List<AnthropicModel>? Data { get; set; }
    }

    internal sealed class AnthropicModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}
