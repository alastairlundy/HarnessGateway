namespace HarnessGateway.Launcher.Tests;

/// <summary>
/// Runs one test body with <see cref="Console.Out"/> redirected to an
/// in-memory writer and hands back whatever was written. Console writers are
/// process-global while TUnit runs tests in parallel, so this capture shares
/// <see cref="ConsoleErrorCapture"/>'s gate: a second independent gate would
/// race under parallel runs and cause the cascading ObjectDisposedException
/// that gate exists to prevent. Because output from tests that do not capture
/// can land in the window, assertions on the captured text use Contains, not
/// whole-stream equality.
/// </summary>
#pragma warning disable TUnit0055 // Console redirection, serialized by ConsoleErrorCapture.Gate.
public static class ConsoleOutputCapture
{
    public static async Task<(T Result, string Output)> RunAsync<T>(Func<Task<T>> body)
    {
        int previous = ConsoleErrorCapture.Depth.Value;
        bool ownsGate = previous == 0;
        ConsoleErrorCapture.Depth.Value = previous + 1;
        if (ownsGate)
        {
            await ConsoleErrorCapture.Gate.WaitAsync().ConfigureAwait(false);
        }

        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);

        try
        {
            T result = await body().ConfigureAwait(false);
            return (result, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
            if (ownsGate)
            {
                ConsoleErrorCapture.Gate.Release();
            }
            ConsoleErrorCapture.Depth.Value = previous;
        }
    }
}
#pragma warning restore TUnit0055