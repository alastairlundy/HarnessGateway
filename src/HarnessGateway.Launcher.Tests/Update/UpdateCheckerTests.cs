using System.Net;
using HarnessGateway.Launcher.Core.Update;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Update;

/// <summary>
/// Component tests for the update check: every
/// outcome is driven through a stub HTTP layer and a fake clock, so the suite
/// never reaches the network and never waits out the real cooldown window or
/// the real request deadline.
/// </summary>
public class UpdateCheckerTests
{
    private static readonly DateTimeOffset Baseline = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-updatecheck-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static UpdateChecker CreateChecker(
        StubHttpMessageHandler handler,
        FakeTimeProvider clock,
        string directory,
        string currentVersion = "0.1.0",
        TimeSpan? requestTimeout = null)
    {
        return new UpdateChecker(
            new HttpClient(handler),
            clock,
            new UpdateCooldownStore(directory),
            currentVersion,
            requestTimeout);
    }

    [Test]
    public async Task Newer_release_tag_reports_update_available()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.2.0"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.UpdateAvailable);
            await Assert.That(result.LatestVersion).IsEqualTo("v0.2.0");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Matching_release_tag_reports_up_to_date()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"0.1.0"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.UpToDate);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Unreachable_endpoint_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
            await Assert.That(result.FailureDetail).IsNotNull();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Deadline_miss_reports_check_failed_naming_the_timeout()
    {
        string dir = CreateTempDir();
        try
        {
            // The stub throws the cancellation the 30-second deadline would
            // produce, so the suite never waits out the real bound.
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new TaskCanceledException());
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
            await Assert.That(result.FailureDetail).Contains("timed out");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Check_completes_within_thirty_seconds_when_the_endpoint_hangs()
    {
        string dir = CreateTempDir();
        try
        {
            // The endpoint never answers, so only the request deadline can
            // end the check. The deadline is shortened to skip the real
            // 30-second wait; the production value itself is pinned by the
            // RequestTimeout test below.
            HangingHttpMessageHandler handler = new();
            UpdateChecker checker = new(
                new HttpClient(handler),
                new FakeTimeProvider(Baseline),
                new UpdateCooldownStore(dir),
                currentVersion: "0.1.0",
                TimeSpan.FromMilliseconds(250));

            Task<UpdateCheckResult> check = checker.CheckAsync(force: false);

            // The expectation under test: one update check never takes
            // longer than 30 seconds - the deadline ends a hung endpoint
            // instead of riding the HTTP client's 100-second default or
            // hanging forever.
            await Assert.That(async () => { await check; }).CompletesWithin(TimeSpan.FromSeconds(30));

            UpdateCheckResult result = await check;

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
            await Assert.That(result.FailureDetail).Contains("timed out");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task RequestTimeout_is_thirty_seconds()
    {
        // Subject held in a local so the assertion is not on a constant.
        TimeSpan bound = UpdateChecker.RequestTimeout;
        await Assert.That(bound).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Caller_cancellation_rethrows_instead_of_mapping_to_an_outcome()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new OperationCanceledException());
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            // Cancellation is not a check outcome: it escapes CheckAsync
            // rather than folding into CheckFailed.
            await Assert.That(async () => await checker.CheckAsync(force: false, cancelled.Token))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Unparseable_response_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("this is not json");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Response_without_a_tag_name_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"name":"no tag here"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Unparseable_release_tag_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"next-release"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Non_success_status_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new(null, HttpStatusCode.ServiceUnavailable);
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Unparseable_build_version_reports_check_failed()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.2.0"}""");
            UpdateChecker checker = CreateChecker(
                handler,
                new FakeTimeProvider(Baseline),
                dir,
                currentVersion: "not-a-version");

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Request_targets_the_github_latest_endpoint_unauthenticated()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            await checker.CheckAsync(force: false);

            await Assert.That(handler.CallCount).IsEqualTo(1);
            await Assert.That(handler.LastRequest).IsNotNull();
            await Assert.That(handler.LastRequest.RequestUri).IsEqualTo(
                new Uri("https://api.github.com/repos/alastairlundy/HarnessGateway/releases/latest"));

            // Unauthenticated only: no credential rides along, and the
            // User-Agent GitHub's API requires is present.
            await Assert.That(handler.LastRequest.Headers.Authorization).IsNull();
            await Assert.That(handler.LastRequest.Headers.UserAgent.ToString()).IsNotEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task First_check_stamps_the_cooldown_timestamp()
    {
        string dir = CreateTempDir();
        try
        {
            UpdateCooldownStore store = new(dir);
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            await checker.CheckAsync(force: false);

            await Assert.That(store.ReadLastCheck()).IsEqualTo(Baseline);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Second_check_inside_the_window_skips_the_network_and_reports_remaining()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            FakeTimeProvider clock = new(Baseline);
            UpdateChecker checker = CreateChecker(handler, clock, dir);

            await checker.CheckAsync(force: false);
            await Assert.That(handler.CallCount).IsEqualTo(1);

            clock.Advance(TimeSpan.FromSeconds(1));
            UpdateCheckResult second = await checker.CheckAsync(force: false);

            await Assert.That(second.Outcome).IsEqualTo(UpdateCheckOutcome.CooldownActive);
            await Assert.That(second.CooldownRemaining).IsEqualTo(TimeSpan.FromSeconds(599));
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Check_after_the_window_reaches_the_network_again()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            FakeTimeProvider clock = new(Baseline);
            UpdateChecker checker = CreateChecker(handler, clock, dir);

            await checker.CheckAsync(force: false);
            clock.Advance(UpdateChecker.CooldownWindow);

            UpdateCheckResult afterWindow = await checker.CheckAsync(force: false);

            await Assert.That(afterWindow.Outcome).IsEqualTo(UpdateCheckOutcome.UpToDate);
            await Assert.That(handler.CallCount).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Force_check_bypasses_the_cooldown_window()
    {
        string dir = CreateTempDir();
        try
        {
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            FakeTimeProvider clock = new(Baseline);
            UpdateChecker checker = CreateChecker(handler, clock, dir);

            await checker.CheckAsync(force: false);
            clock.Advance(TimeSpan.FromSeconds(1));

            UpdateCheckResult forced = await checker.CheckAsync(force: true);

            await Assert.That(forced.Outcome).IsNotEqualTo(UpdateCheckOutcome.CooldownActive);
            await Assert.That(handler.CallCount).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Failed_check_stamps_the_window_too()
    {
        // A failed call still spent rate-limit budget, so the retry inside
        // the window is skipped like any other.
        string dir = CreateTempDir();
        try
        {
            UpdateCooldownStore store = new(dir);
            StubHttpMessageHandler handler = new(
                body: null,
                failure: new HttpRequestException("connection refused"));
            FakeTimeProvider clock = new(Baseline);
            UpdateChecker checker = CreateChecker(handler, clock, dir);

            UpdateCheckResult failed = await checker.CheckAsync(force: false);
            await Assert.That(failed.Outcome).IsEqualTo(UpdateCheckOutcome.CheckFailed);
            await Assert.That(store.ReadLastCheck()).IsEqualTo(Baseline);

            clock.Advance(TimeSpan.FromSeconds(1));
            UpdateCheckResult retry = await checker.CheckAsync(force: false);

            await Assert.That(retry.Outcome).IsEqualTo(UpdateCheckOutcome.CooldownActive);
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Damaged_timestamp_file_is_treated_as_no_last_check()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, UpdateCooldownStore.FileName), "not-a-timestamp");
            StubHttpMessageHandler handler = new("""{"tag_name":"v0.1.0"}""");
            UpdateChecker checker = CreateChecker(handler, new FakeTimeProvider(Baseline), dir);

            UpdateCheckResult result = await checker.CheckAsync(force: false);

            await Assert.That(result.Outcome).IsEqualTo(UpdateCheckOutcome.UpToDate);
            await Assert.That(handler.CallCount).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
