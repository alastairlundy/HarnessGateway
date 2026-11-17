namespace HarnessGateway.Launcher.Tests.Fakes;

/// <summary>
/// Settable stand-in for <see cref="TimeProvider.System"/>: the update-check
/// cooldown is measured against this clock, so tests advance time instead of
/// waiting out the real 10-minute window.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
