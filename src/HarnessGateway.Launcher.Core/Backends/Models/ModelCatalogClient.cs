using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Backends.Models;

public sealed class ModelCatalogClient
{
    private readonly HttpClient _httpClient;

    public ModelCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ModelCatalog> FetchModelsAsync(Backend backend, CancellationToken cancellationToken = default)
    {
        try
        {
            return backend.Protocol switch
            {
                ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => await FetchOpenAiModelsAsync(backend, cancellationToken),
                ProtocolKind.Anthropic => await FetchAnthropicModelsAsync(backend, cancellationToken),
                _ => new ModelCatalog([], DateTimeOffset.UtcNow)
            };
        }
        catch
        {
            return new ModelCatalog([], DateTimeOffset.UtcNow);
        }
    }

    private async Task<ModelCatalog> FetchOpenAiModelsAsync(Backend backend, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");

        if (!string.IsNullOrEmpty(backend.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", backend.ApiKey);
        }

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        OpenAiModelsResponse? openAiResponse = await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(cancellationToken: cancellationToken);

        List<ModelCatalogEntry> models = openAiResponse?.Data?.Select(m => new ModelCatalogEntry(m.Id, m.Id)).ToList() ?? [];

        return new ModelCatalog(models, DateTimeOffset.UtcNow);
    }

    private async Task<ModelCatalog> FetchAnthropicModelsAsync(Backend backend, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Add("anthropic-version", "2023-06-01");

        if (!string.IsNullOrEmpty(backend.ApiKey))
        {
            request.Headers.Add("x-api-key", backend.ApiKey);
        }

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        AnthropicModelsResponse? anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicModelsResponse>(cancellationToken: cancellationToken);

        List<ModelCatalogEntry> models = anthropicResponse?.Data?.Select(m => new ModelCatalogEntry(m.Id, m.DisplayName)).ToList() ?? [];

        return new ModelCatalog(models, DateTimeOffset.UtcNow);
    }

    private sealed class OpenAiModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiModel>? Data { get; set; }
    }

    private sealed class OpenAiModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("object")]
        public string? Object { get; set; }
    }

    private sealed class AnthropicModelsResponse
    {
        [JsonPropertyName("data")]
        public List<AnthropicModel>? Data { get; set; }
    }

    private sealed class AnthropicModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}
