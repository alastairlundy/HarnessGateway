namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// macOS implementation: generic password items in the login keychain, keyed by
/// service <c>HarnessGateway</c> and account = provider name, driven through the
/// <c>/usr/bin/security</c> command line tool.
/// </summary>
public sealed class MacOsKeychainSecureKeyStore : ISecureKeyStore
{
    private const string SecurityBinary = "/usr/bin/security";
    private const string ServiceName = "HarnessGateway";

    private const string StoreOperation = "save to the macOS Keychain";
    private const string LoadOperation = "load from the macOS Keychain";
    private const string DeleteOperation = "delete from the macOS Keychain";

    private const string SpawnFailure =
        "the '/usr/bin/security' command could not be run";

    public MacOsKeychainSecureKeyStore()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw SecureKeyStoreException.UnsupportedPlatform(
                "the macOS Keychain store only runs on macOS.");
        }
    }

    public async Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        string providerName = KeyReference.RequireProviderName(reference);

        // `-w` with no value keeps the secret off argv - a value on argv would be
        // visible to same-user `ps` and to shell history when invoked by hand.
        // Residual risk: security(1) does not document the valueless `-w` stdin
        // read, so this path still needs a round-trip check on a real Mac; the
        // documented fallback is `-w <secret>` on argv, which restores that
        // same-user `ps` exposure.
        using MemoryStream payload = new(Encoding.UTF8.GetBytes(secret));
        using StreamWriter writer = new(
            payload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);
        payload.Position = 0;

        BufferedProcessResult result = await SecureKeyStoreProcess.RunAsync(
            SecurityBinary,
            ["add-generic-password", "-U", "-s", ServiceName, "-a", providerName, "-w"],
            providerName,
            StoreOperation,
            SpawnFailure,
            writer,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw SecureKeyStoreException.OperationFailed(
                providerName,
                StoreOperation,
                "the 'security' command rejected the item; unlock the login keychain and retry.");
        }
    }

    public async Task<string> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        string providerName = KeyReference.RequireProviderName(reference);

        BufferedProcessResult result = await SecureKeyStoreProcess.RunAsync(
            SecurityBinary,
            ["find-generic-password", "-s", ServiceName, "-a", providerName, "-w"],
            providerName,
            LoadOperation,
            SpawnFailure,
            standardInput: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // A non-zero exit is the miss path: no item, or an item this user may not
        // read. Either way the store reports "no entry" - now by throwing.
        if (result.ExitCode != 0)
        {
            throw SecureKeyStoreException.AbsentKey(reference);
        }

        // Trailing newline only - the stored value is ASCII, so the macOS 26
        // hex-encoding of *binary* password data never applies here.
        string password = result.StandardOutput.TrimEnd('\r', '\n');
        if (password.Length == 0)
        {
            // The item exists but yielded no secret: the tool did not produce a
            // usable value, which is a store failure rather than an absence.
            throw SecureKeyStoreException.SecureOperationFailed(
                "macOS Keychain",
                "the 'security' tool returned an empty secret.");
        }

        return password;
    }

    public async Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        string providerName = KeyReference.RequireProviderName(reference);

        // Both the idempotent miss and an operational failure exit non-zero:
        // only the recognized "could not be found" miss succeeds (see
        // MacOsDeleteFailed) - everything else, a locked keychain or an
        // authorization refusal, is reported instead of swallowed.
        BufferedProcessResult result = await SecureKeyStoreProcess.RunAsync(
            SecurityBinary,
            ["delete-generic-password", "-s", ServiceName, "-a", providerName],
            providerName,
            DeleteOperation,
            SpawnFailure,
            standardInput: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (SecureKeyStoreProcess.MacOsDeleteFailed(result.ExitCode, result.StandardError))
        {
            throw SecureKeyStoreException.OperationFailed(
                providerName,
                DeleteOperation,
                "the 'security' command could not delete the item; unlock the login keychain and retry.");
        }
    }
}
