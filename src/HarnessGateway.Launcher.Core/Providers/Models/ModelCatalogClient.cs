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
    /// Builds the absolute model-list request URI from the provider's own
    /// endpoint so the request never depends on <see cref="HttpClient.BaseAddress"/>.
    /// The endpoint's path is preserved and <c>/models</c> is appended to it,
    /// whatever that path is: a <c>/v1</c> base still yields <c>/v1/models</c>
    /// (no doubled <c>/v1</c>), a prefixed base such as <c>/openai</c> yields
    /// <c>/openai/models</c> instead of silently dropping the prefix, and a base
    /// already ending in <c>/models</c> is not doubled. A bare origin (no path)
    /// keeps the conventional <c>/v1/models</c> target it has always fetched.
    /// The base's query and fragment stay unchanged (gated proxies using
    /// <c>?key=...</c> keep working, as they do on the launch path), and a path
    /// starting with <c>//</c> is collapsed to a single leading <c>/</c> so it can
    /// never resolve scheme-relative against the authority and swap the host.
    /// </summary>
    private static Uri BuildModelsUri(Provider backend)
    {
        // When the provider speaks the OpenAI Responses API, a base URL that
        // already ends in "/responses" would yield "/responses/models" here;
        // strip a single trailing occurrence so the models request lands on
        // "/v1/models" (or "/models") instead. Shared with the launch-side
        // normalisation via EndpointText.
        string basePath = backend.DefaultProtocol == ProtocolKind.OpenAiResponses
            ? EndpointText.TrimTrailingResponsesSegment(backend.BaseUri.AbsolutePath)
            : backend.BaseUri.AbsolutePath.TrimEnd('/');

        // A bare origin trims down to an empty path; keep its historical
        // /v1 target so https://host still fetches https://host/v1/models.
        if (basePath.Length == 0)
        {
            basePath = "/v1";
        }

        // Collapse a leading "//" so the path can never be read as
        // scheme-relative (which would replace the host with the first segment).
        basePath = "/" + basePath.TrimStart('/');

        string modelsPath = basePath.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? basePath
            : basePath + "/models";

        // UriBuilder keeps the base's scheme, host, port, query, and fragment
        // while swapping in the derived path - GetLeftPart(UriPartial.Authority)
        // would have dropped the query.
        UriBuilder builder = new UriBuilder(backend.BaseUri) { Path = modelsPath };
        return builder.Uri;
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
