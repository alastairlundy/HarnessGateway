using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings.Security;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Settings;

public class BackendKeyResolverTests
{
    private static Backend Entry(string? apiKey) => new()
    {
        Name = "default",
        BaseUri = new Uri("https://api.example.com/v1"),
        Protocol = ProtocolKind.OpenAiChat,
        ApiKey = apiKey
    };

    [Test]
    public async Task Null_ApiKey_returns_unchanged_without_touching_the_store()
    {
        FakeSecureKeyStore keyStore = new();

        Backend resolved = await BackendKeyResolver.ResolveAsync(Entry(null), keyStore);

        await Assert.That(resolved.ApiKey).IsNull();
        await Assert.That(keyStore.RetrieveCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reference_resolves_to_the_stored_secret_on_a_fresh_record()
    {
        FakeSecureKeyStore keyStore = new();
        keyStore.Seed("hgkey:default", "sk-stored");
        Backend entry = Entry("hgkey:default");

        Backend resolved = await BackendKeyResolver.ResolveAsync(entry, keyStore);

        await Assert.That(resolved.ApiKey).IsEqualTo("sk-stored");
        // The settings-owned record keeps the reference; only the returned copy
        // carries the secret, so no Save path can write a secret back out.
        await Assert.That(entry.ApiKey).IsEqualTo("hgkey:default");
    }

    [Test]
    public async Task Dangling_reference_throws_the_store_absent_key()
    {
        FakeSecureKeyStore keyStore = new();

        Exception? caught = null;
        try
        {
            await BackendKeyResolver.ResolveAsync(Entry("hgkey:default"), keyStore);
        }
        catch (SecureKeyStoreException ex)
        {
            caught = ex;
        }

        // The store classifies absence at its source; the resolver no longer
        // wraps it, so the message the caller sees is the store's.
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message)
            .IsEqualTo(SecureKeyStoreException.AbsentKey("hgkey:default").Message);
    }

    [Test]
    public async Task Legacy_plaintext_key_passes_through_untouched()
    {
        // Decision 1: only 007 ever writes references; an older plaintext value
        // stays usable in memory until ticket 008 migrates it.
        FakeSecureKeyStore keyStore = new();
        Backend entry = Entry("sk-legacy");

        Backend resolved = await BackendKeyResolver.ResolveAsync(entry, keyStore);

        await Assert.That(resolved.ApiKey).IsEqualTo("sk-legacy");
        await Assert.That(keyStore.RetrieveCallCount).IsEqualTo(0);
    }
}
