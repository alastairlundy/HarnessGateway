using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Tests.Settings;

public class KeyReferenceTests
{
    [Test]
    public async Task ForBackend_produces_the_prefix_format()
    {
        string reference = KeyReference.ForBackend("default");

        await Assert.That(reference).IsEqualTo("hgkey:default");
    }

    [Test]
    public async Task IsReference_accepts_a_reference_and_rejects_real_keys()
    {
        bool isReference = KeyReference.IsReference("hgkey:default");
        bool isRealKey = KeyReference.IsReference("sk-ant-api-key");
        bool isNull = KeyReference.IsReference(null);
        bool isEmpty = KeyReference.IsReference(string.Empty);
        bool isPrefixOnly = KeyReference.IsReference("hgkey:");

        await Assert.That(isReference).IsTrue();
        await Assert.That(isRealKey).IsFalse();
        await Assert.That(isNull).IsFalse();
        await Assert.That(isEmpty).IsFalse();
        await Assert.That(isPrefixOnly).IsFalse();
    }

    [Test]
    public async Task BackendName_round_trips_through_ForBackend()
    {
        string reference = KeyReference.ForBackend("secondary");

        string? backendName = KeyReference.BackendName(reference);

        await Assert.That(backendName).IsEqualTo("secondary");
    }

    [Test]
    public async Task BackendName_returns_null_for_a_legacy_plaintext_key()
    {
        string? backendName = KeyReference.BackendName("sk-legacy-key");

        await Assert.That(backendName).IsNull();
    }

    [Test]
    public async Task RequireBackendName_throws_without_echoing_the_reference()
    {
        Exception? caught = null;
        try
        {
            KeyReference.RequireBackendName("not-a-reference");
        }
        catch (SecureKeyStoreException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("not a HarnessGateway key reference");
        await Assert.That(caught.Message.Contains("not-a-reference")).IsFalse();
    }
}
