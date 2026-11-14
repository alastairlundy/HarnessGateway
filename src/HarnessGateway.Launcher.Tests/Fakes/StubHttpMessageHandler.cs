using System.Net;
using System.Text;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Scripted <see cref="HttpMessageHandler"/> standing in for the network: it
/// answers with a canned status and body, optionally throws instead, and
/// counts every call so a test can prove how many times the network was -
/// or, for the cooldown, was not - reached.
/// </summary>
public sealed class StubHttpMessageHandler(
    string? body,
    HttpStatusCode statusCode = HttpStatusCode.OK,
    Exception? failure = null)
    : HttpMessageHandler
{
    public int CallCount { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        if (failure is not null)
        {
            throw failure;
        }

        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json"),
        });
    }
}
