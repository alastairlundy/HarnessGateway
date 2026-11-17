using System.Diagnostics;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// HTTP handler standing in for a hung endpoint: it never produces a
/// response and returns only when the token it is handed fires, throwing
/// the cancellation onward exactly as a cancelled request would. It lets
/// tests prove the update-check deadline cuts off a never-completing
/// endpoint without a real 30-second wait.
/// </summary>
public sealed class HangingHttpMessageHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        throw new UnreachableException("The hanging handler completed without cancellation.");
    }
}
