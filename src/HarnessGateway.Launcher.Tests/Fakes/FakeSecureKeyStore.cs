using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISecureKeyStore"/> for the test gate: no keychain, no
/// external process, and no launcher-owned file. Call counters let tests prove
/// that a keyless backend never reaches the store.
/// </summary>
public sealed class FakeSecureKeyStore : ISecureKeyStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public int StoreCallCount { get; private set; }
    public int RetrieveCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }

    public bool FailStore { get; set; }
    public bool FailDelete { get; set; }

    public void Seed(string reference, string secret) => _secrets[reference] = secret;

    public bool Contains(string reference) => _secrets.ContainsKey(reference);

    public Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        StoreCallCount++;

        if (FailStore)
        {
            throw SecureKeyStoreException.OperationFailed(
                KeyReference.BackendName(reference) ?? "unknown",
                "save to the platform credential store",
                "the fake key store is configured to fail.");
        }

        _secrets[reference] = secret;
        return Task.CompletedTask;
    }

    public Task<string> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        RetrieveCallCount++;

        if (!_secrets.TryGetValue(reference, out string? secret))
        {
            throw SecureKeyStoreException.AbsentKey(reference);
        }

        return Task.FromResult(secret);
    }

    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;

        if (FailDelete)
        {
            throw SecureKeyStoreException.OperationFailed(
                KeyReference.BackendName(reference) ?? "unknown",
                "remove from the platform credential store",
                "the fake key store is configured to fail.");
        }

        _secrets.Remove(reference);
        return Task.CompletedTask;
    }
}
