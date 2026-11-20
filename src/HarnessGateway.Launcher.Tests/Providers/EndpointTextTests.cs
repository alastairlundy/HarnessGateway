using HarnessGateway.Launcher.Core.Providers;

namespace HarnessGateway.Launcher.Tests.Providers;

public class EndpointTextTests
{
    [Test]
    public async Task TrimTrailingPathSlash_trims_a_path_slash_and_a_bare_origin()
    {
        // The two common contracts: a /v1/ base reaches the harness as /v1,
        // and a bare origin keeps dropping its root slash.
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1/"))).IsEqualTo("https://host/v1");
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/"))).IsEqualTo("https://host");
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1"))).IsEqualTo("https://host/v1");
    }

    [Test]
    public async Task TrimTrailingPathSlash_does_not_mangle_a_query_ending_in_a_slash()
    {
        // Trailing-slash trimming is a path concern only: trimming the whole
        // string used to chop a query's final "/" off (https://host/v1?next=/
        // became https://host/v1?next=), corrupting it.
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1?next=/")))
            .IsEqualTo("https://host/v1?next=/");
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1/?next=/")))
            .IsEqualTo("https://host/v1?next=/");
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1?key=abc")))
            .IsEqualTo("https://host/v1?key=abc");
    }

    [Test]
    public async Task TrimTrailingPathSlash_trims_before_a_fragment_only()
    {
        // The fragment rides along untouched; the slash before it is still
        // part of the path and is trimmed.
        await Assert.That(
            EndpointText.TrimTrailingPathSlash(new Uri("https://host/v1/#top")))
            .IsEqualTo("https://host/v1#top");
    }
}
