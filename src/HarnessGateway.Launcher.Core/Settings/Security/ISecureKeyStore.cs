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

    /// <summary>Returns the secret for <paramref name="reference"/>, or null when the store holds no entry.</summary>
    Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry for <paramref name="reference"/>. Idempotent: removing a missing entry succeeds.</summary>
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Failure raised by a secure-key-store operation. Messages name the backend and
/// the re-save instruction, and never carry secret or reference material beyond
/// the backend name. Callers map this to exit code 7 (<c>ConfigRestoreFailure</c>).
/// </summary>
public sealed class SecureKeyStoreException : Exception
{
    public SecureKeyStoreException(string message)
        : base(message)
    {
    }

    public SecureKeyStoreException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// A reference is present at rest but the platform credential store holds no
    /// secret for it - the entry was deleted, migrated away, or copied from
    /// another machine where it cannot be decrypted.
    /// </summary>
    public static SecureKeyStoreException DanglingReference(string backendName) =>
        new(
            $"No stored API key found for backend '{backendName}'. " +
            "Re-save it with 'harnessgateway config set-saved-key <key>'.");

    /// <summary>
    /// The platform credential store refused or failed an operation. The detail is
    /// curated by the implementation - it is never an arbitrary exception message,
    /// which could otherwise echo secret material.
    /// </summary>
    public static SecureKeyStoreException OperationFailed(
        string backendName,
        string operation,
        string detail,
        Exception? innerException = null) =>
        new(
            $"Could not {operation} for backend '{backendName}': {detail} " +
            "Re-save the key with 'harnessgateway config set-saved-key <key>'.",
            innerException);

    /// <summary>No platform credential store exists for this machine at all.</summary>
    public static SecureKeyStoreException UnsupportedPlatform(string detail) =>
        new($"The platform credential store is unavailable: {detail}");
}
