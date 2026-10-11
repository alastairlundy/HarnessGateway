namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// The single surface through which saved API keys reach a platform-native
/// credential store. Implementations key their entries by an opaque
/// <c>hgkey:&lt;backend-name&gt;</c> reference (see <see cref="KeyReference"/>);
/// neither this interface nor its implementations ever persist secret material
/// in launcher-owned files.
/// </summary>
public interface ISecureKeyStore
{
    /// <summary>Stores (or overwrites) the secret for <paramref name="reference"/>.</summary>
    Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the secret for <paramref name="reference"/>. Throws
    /// <see cref="SecureKeyStoreException"/> when the store holds no entry or
    /// the platform credential store fails - callers never receive null.
    /// </summary>
    Task<string> RetrieveAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry for <paramref name="reference"/>. Idempotent: removing a missing entry succeeds.</summary>
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}