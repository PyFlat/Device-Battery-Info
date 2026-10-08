namespace DeviceBatteryInfo.Core;

// The host throttles all of a plugin's callbacks together, adb included, so any call can be refused
// while the poll is busy; the budget refills within seconds.
internal static class HostCallRetry
{
    private const int MaxAttempts = 6;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(250);

    public static async Task<T> RunAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransient(exception))
            {
                await Task.Delay(FirstDelay * Math.Pow(2, attempt - 1), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    // HostInvocationException is internal to the hosting package, so it is matched by name.
    private static bool IsTransient(Exception exception) =>
        exception.GetType().Name == "HostInvocationException"
        && (
            exception.Message.Contains("too quickly", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
        );
}
