namespace HarnessGateway.Launcher.Core.Settings.Security;

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
    /// True only for the <see cref="AbsentKey(string)"/> variant: the reference
    /// is fine but the store holds no secret for it. Every variant is this one
    /// class, so this marker is the fold seam the launch flow reads to separate
    /// that user-rectifiable miss from an operational credential-store failure.
    /// </summary>
    public bool IsAbsentKey { get; private init; }

    /// <summary>
    /// A reference is present at rest but the platform credential store holds no
    /// secret for it - the entry was deleted, migrated away, or copied from
    /// another machine where it cannot be decrypted.
    /// </summary>
    public static SecureKeyStoreException DanglingReference(string backendName) =>
        new(
            $"No stored API key found for backend '{backendName}'. " +
            $"Re-save it with 'harnessgateway config backend set-key {backendName}'.");

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
            $"Re-save the key with 'harnessgateway config backend set-key {backendName}'.",
            innerException);

    /// <summary>
    /// The store holds no entry for <paramref name="reference"/> - the expected
    /// miss when a key was never saved, was deleted, or cannot be decrypted on
    /// this machine. The reference is the opaque key name, never secret material.
    /// </summary>
    public static SecureKeyStoreException AbsentKey(string reference) =>
        new($"No stored API key found for '{reference}'.")
        {
            IsAbsentKey = true,
        };

    /// <summary>
    /// The named platform credential store refused or failed an operation.
    /// The detail is curated by the implementation - it is never an arbitrary
    /// exception message, which could otherwise echo secret material.
    /// </summary>
    public static SecureKeyStoreException SecureOperationFailed(
        string storeName,
        string detail) =>
        new($"The platform credential store '{storeName}' failed: {detail}");

    /// <summary>No platform credential store exists for this machine at all.</summary>
    public static SecureKeyStoreException UnsupportedPlatform(string detail) =>
        new($"The platform credential store is unavailable: {detail}");
}