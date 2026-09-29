using System.Text;

namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Linux implementation: Secret Service entries via the <c>secret-tool</c>
/// (libsecret) command line tool, carrying the backend name as a custom
/// <c>key</c> attribute.
/// </summary>
public sealed class LinuxSecretServiceSecureKeyStore : ISecureKeyStore
{
    private const string SecretTool = "secret-tool";

    private const string StoreOperation = "save to the Secret Service keyring";
    private const string LoadOperation = "load from the Secret Service keyring";
    private const string DeleteOperation = "delete from the Secret Service keyring";

    private const string SpawnFailure =
        "'secret-tool' was not found or could not be run; install the libsecret command line tools " +
        "(for example 'libsecret-tools') and ensure 'secret-tool' is on PATH";

    private const string NoSessionFailure =
        "no Secret Service keyring session is available; unlock or start a desktop keyring " +
        "(headless sessions need a Secret Service provider such as gnome-keyring or KWallet)";

    public LinuxSecretServiceSecureKeyStore()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw SecureKeyStoreException.UnsupportedPlatform(
                "the Secret Service store only runs on Linux.");
        }
    }

    public async Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);

        // The secret rides on stdin, never on argv.
        using MemoryStream payload = new(Encoding.UTF8.GetBytes(secret));
        using StreamWriter writer = new(
            payload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);
        payload.Position = 0;

        BufferedProcessResult result = await SecureKeyStoreProcess.RunAsync(
            SecretTool,
            ["store", "--label", $"HarnessGateway {backendName}", "key", backendName],
            backendName,
            StoreOperation,
            SpawnFailure,
            writer,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, StoreOperation, NoSessionFailure + ".");
        }
    }

    public async Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);

        BufferedProcessResult result = await SecureKeyStoreProcess.RunAsync(
            SecretTool,
            ["lookup", "key", backendName],
            backendName,
            LoadOperation,
            SpawnFailure,
            standardInput: null,
            cancellationToken: cancellationToken);

        // A miss and an unreachable keyring both exit non-zero and are
        // indistinguishable here; the keyring case surfaces with its own
        // instructive message the first time a key is stored (above).
        if (result.ExitCode != 0)
        {
            return null;
        }

        string password = result.StandardOutput.TrimEnd('\r', '\n');
        return password.Length == 0 ? null : password;
    }

    public async Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);

        // Best effort: `clear` on an entry that is not there must still succeed
        // for the idempotent contract, so a non-zero exit is swallowed here too.
        await SecureKeyStoreProcess.RunAsync(
            SecretTool,
            ["clear", "key", backendName],
            backendName,
            DeleteOperation,
            SpawnFailure,
            standardInput: null,
            cancellationToken: cancellationToken);
    }
}
