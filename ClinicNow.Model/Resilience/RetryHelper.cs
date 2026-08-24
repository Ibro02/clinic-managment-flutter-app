namespace ClinicNow.Model.Resilience;

/// <summary>
/// Small, shared exponential-backoff retry helper (1s, 2s, 4s, 8s by default),
/// used wherever this app talks to an external dependency that may still be
/// starting up under docker-compose - SQL Server, RabbitMQ (rulebook Appendix
/// A.1: "implementirati retry logiku sa eksponencijalnim backoff-om").
///
/// Kept here in Model so both ClinicNow.API (DB migration at startup) and
/// ClinicNow.Worker (RabbitMQ connection) share one implementation instead of
/// duplicating the same loop twice (DRY - rulebook Part II §D).
/// </summary>
public static class RetryHelper
{
    public static readonly TimeSpan[] DefaultBackoffDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    /// <summary>
    /// Runs <paramref name="action"/>, retrying with exponential backoff on failure.
    /// <paramref name="onRetry"/> fires before each wait (e.g. to log a warning) -
    /// silent retries are exactly what the rulebook forbids. Once <paramref name="delays"/>
    /// is exhausted, the last exception is rethrown rather than swallowed.
    /// </summary>
    public static async Task RunWithRetryAsync(
        Func<Task> action,
        Action<Exception, int, TimeSpan> onRetry,
        CancellationToken cancellationToken,
        TimeSpan[]? delays = null)
    {
        delays ??= DefaultBackoffDelays;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < delays.Length)
            {
                var delay = delays[attempt];
                onRetry(ex, attempt + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
