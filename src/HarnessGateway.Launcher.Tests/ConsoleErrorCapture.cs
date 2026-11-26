namespace HarnessGateway.Launcher.Tests;

/// <summary>
/// Runs one test body with <see cref="Console.Error"/> redirected to an
/// in-memory writer and hands back whatever was written. Console.Error is
/// process-global while TUnit runs tests in parallel, so captures are
/// serialized and always restored: an overlapping test capturing a writer this
/// scope later disposes turns into a cascading ObjectDisposedException.
/// <see cref="ConsoleOutputCapture"/> shares this class's gate - never add a
/// second independent gate.
/// </summary>
#pragma warning disable TUnit0055 // Console redirection, serialized by this class's gate.
public static class ConsoleErrorCapture
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
    internal static readonly AsyncLocal<int> Depth = new();

    public static bool IsHeld => Depth.Value > 0;

    public static async Task<(T Result, string Error)> RunAsync<T>(Func<Task<T>> body)
    {
        int previous = Depth.Value;
        bool ownsGate = previous == 0;
        Depth.Value = previous + 1;
        if (ownsGate)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
        }

        TextWriter original = Console.Error;
        using StringWriter writer = new();
        Console.SetError(writer);

        try
        {
            T result = await body().ConfigureAwait(false);
            return (result, writer.ToString());
        }
        finally
        {
            Console.SetError(original);
            if (ownsGate)
            {
                Gate.Release();
            }
            Depth.Value = previous;
        }
    }
}
#pragma warning restore TUnit0055