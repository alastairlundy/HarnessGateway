namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// The one read rule for turning a stored <c>Backend.ApiKey</c> into a secret:
/// a <see cref="KeyReference"/> resolves through the <see cref="ISecureKeyStore"/>,
/// a null key stays null without touching the store (D012: no dummy values), and
/// any other value is a legacy plaintext key used as-is until ticket 008 migrates it.
/// Always returns a fresh record, so the settings object itself is never mutated
/// with secrets and every <c>Save</c> path keeps writing references.
/// </summary>
public static class BackendKeyResolver
{
    public static async Task<Backend> ResolveAsync(
        Backend backend,
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

        string? secret = await keyStore.RetrieveAsync(backend.ApiKey, cancellationToken);
        if (secret is null)
        {
            throw SecureKeyStoreException.DanglingReference(backend.Name);
        }

        return backend with { ApiKey = secret };
    }
}
