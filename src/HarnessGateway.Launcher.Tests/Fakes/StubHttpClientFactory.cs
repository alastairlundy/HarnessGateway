namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// The transport seam beneath the models.dev client: the package's
/// repositories fetch through the <see cref="IHttpClientFactory"/> the
/// composition registers, so wrapping a scripted handler here swaps the
/// whole transport without a network call. One client shares one
/// handler, and the handler's call count is how a test proves how many
/// fetches - if any - the cache actually attempted.
/// </summary>
public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
