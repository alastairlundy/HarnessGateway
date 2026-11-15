using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace HarnessGateway.Launcher.Core.Update;

/// <summary>
/// The update check itself: compares this build's version against the
/// latest published GitHub release and reports one of the
/// <see cref="UpdateCheckOutcome"/> values. It is check-only - nothing is
/// ever downloaded, installed, or applied - and the cooldown keeps
/// repeat invocations inside the window from reaching the unauthenticated
/// rate limit.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>The cooldown window: within it a non-forced check makes no network call.</summary>
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// GitHub's Releases latest endpoint for this repository. This is
    /// the only version source: NuGet-based sources are deliberately refused,
    /// no token is ever attached, and the endpoint's own filtering decides
    /// which release counts as latest.
    /// </summary>
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/alastairlundy/HarnessGateway/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateCooldownStore _cooldownStore;
    private readonly string _currentVersion;

    public UpdateChecker(
        HttpClient httpClient,
        TimeProvider timeProvider,
        UpdateCooldownStore cooldownStore,
        string currentVersion)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _cooldownStore = cooldownStore;
        _currentVersion = currentVersion;
    }

    /// <summary>This build's version, for the CLI's report lines.</summary>
    public string CurrentVersion => _currentVersion;

    /// <summary>
    /// Runs one check. <paramref name="force"/> skips the cooldown window;
    /// without it a check inside the window returns
    /// <see cref="UpdateCheckOutcome.CooldownActive"/> with the time left and
    /// never touches the network.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!force && _cooldownStore.ReadLastCheck() is { } lastCheck)
        {
            TimeSpan sinceLastCheck = now - lastCheck;
            if (sinceLastCheck < CooldownWindow)
            {
                return new UpdateCheckResult(
                    UpdateCheckOutcome.CooldownActive,
                    CooldownRemaining: CooldownWindow - sinceLastCheck);
            }
        }

        UpdateCheckResult result = await QueryLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

        // Every attempt that reached the network is stamped, failed or not:
        // a failed call still spent unauthenticated rate-limit budget, and
        // the window exists to keep repeat use clear of that budget.
        _cooldownStore.WriteLastCheck(_timeProvider.GetUtcNow());

        return result;
    }

    /// <summary>
    /// Fetches the latest release payload and hands it to the comparison.
    /// Every fault - unreachable host, HTTP error, malformed payload - maps
    /// to <see cref="UpdateCheckOutcome.CheckFailed"/> so no exception
    /// escapes with an exit code outside the contract;
    /// caller-initiated cancellation is the one rethrow.
    /// </summary>
    private async Task<UpdateCheckResult> QueryLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, LatestReleaseEndpoint);

            // GitHub's API rejects requests without a User-Agent. Unauthenticated
            // means exactly that: no Authorization header is ever attached.
            request.Headers.UserAgent.ParseAdd($"HarnessGateway/{_currentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response =
                await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckOutcome.CheckFailed,
                    FailureDetail: $"the GitHub releases endpoint returned HTTP {(int)response.StatusCode}.");
            }

            string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return CompareLatestRelease(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled; cancellation is not a check outcome and
            // must never be mapped onto the exit-code contract.
            throw;
        }
        catch (Exception ex)
        {
            // Unreachable host, timeout, malformed JSON, or a payload shape
            // the reader rejects: all of them are a failed check.
            return new UpdateCheckResult(
                UpdateCheckOutcome.CheckFailed,
                FailureDetail: ex.Message);
        }
    }

    /// <summary>
    /// Reads the tag from one latest-release payload and compares it with
    /// this build. A payload this reader rejects is a failed check,
    /// never an escaping exception.
    /// </summary>
    private UpdateCheckResult CompareLatestRelease(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("tag_name", out JsonElement tagNameElement)
            || tagNameElement.ValueKind != JsonValueKind.String)
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.CheckFailed,
                FailureDetail: "the GitHub releases response carries no tag_name.");
        }

        string tagName = tagNameElement.GetString() ?? string.Empty;
        if (!TryParseVersion(tagName, out Version? latestVersion))
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.CheckFailed,
                FailureDetail: $"the release tag '{tagName}' is not a plain version.");
        }

        if (!TryParseVersion(_currentVersion, out Version? currentVersion))
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.CheckFailed,
                FailureDetail: $"this build's version '{_currentVersion}' is not a plain version.");
        }

        return latestVersion > currentVersion
            ? new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, LatestVersion: tagName)
            : new UpdateCheckResult(UpdateCheckOutcome.UpToDate, LatestVersion: tagName);
    }

    /// <summary>
    /// Parses a release tag or the build's own version, tolerating the leading
    /// <c>v</c> some tags carry and stripping <c>+build-metadata</c> that
    /// <see cref="Version"/> does not model.
    /// </summary>
    private static bool TryParseVersion(string? raw, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string candidate = raw.Trim();
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        int metadataIndex = candidate.IndexOf('+');
        if (metadataIndex >= 0)
        {
            candidate = candidate[..metadataIndex];
        }

        return Version.TryParse(candidate, out version);
    }
}
