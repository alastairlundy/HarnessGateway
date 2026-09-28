namespace HarnessGateway.Launcher.Tests;

/// <summary>
/// Runs one test body with <see cref="Console.Error"/> redirected to an
/// in-memory writer and hands back whatever was written. Console.Error is
/// process-global while TUnit runs tests in parallel, so captures are
/// serialized and always restored: an overlapping test capturing a writer this
/// scope later disposes is what turns into a cascading ObjectDisposedException.
/// </summary>
public static class ConsoleErrorCapture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<(T Result, string Error)> RunAsync<T>(Func<Task<T>> body)
    {
        await Gate.WaitAsync().ConfigureAwait(false);

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
            Gate.Release();
        }
    }
}
