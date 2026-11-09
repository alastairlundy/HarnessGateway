namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// The one read rule for turning a stored <c>Provider.ApiKey</c> into a secret:
/// a <see cref="KeyReference"/> resolves through the <see cref="ISecureKeyStore"/>,
/// a null key stays null without touching the store (no dummy values), and
/// any other value is a legacy plaintext key, used as-is until plaintext
/// keys are migrated.
/// Always returns a fresh record, so the settings object itself is never mutated
/// with secrets and every <c>Save</c> path keeps writing references.
/// </summary>
public static class ProviderKeyResolver
{
    public static async Task<Provider> ResolveAsync(
        Provider backend,
        ISecureKeyStore keyStore,
        CancellationToken cancellationToken = default)
    {
        if (backend.ApiKey is null)
        {
            return backend;
        }

        if (!KeyReference.IsReference(backend.ApiKey))
        {
            return backend;
        }

        string secret = await keyStore.RetrieveAsync(backend.ApiKey, cancellationToken).ConfigureAwait(false);

        return backend with { ApiKey = secret };
    }
}