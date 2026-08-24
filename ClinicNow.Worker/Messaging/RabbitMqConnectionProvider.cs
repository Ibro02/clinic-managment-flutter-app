using ClinicNow.Model.Configuration;
using ClinicNow.Model.Resilience;
using RabbitMQ.Client;

namespace ClinicNow.Worker.Messaging;

/// <summary>
/// Owns exactly one RabbitMQ <see cref="IConnection"/> for the lifetime of the
/// worker process. Per rulebook Appendix A.1 ("Ne kreirati novu RabbitMQ konekciju
/// pri svakom publish pozivu; koristiti singleton konekciju ili connection pool"),
/// nothing else in this project may construct a <see cref="ConnectionFactory"/> or
/// call <c>CreateConnectionAsync</c> directly - everything goes through
/// <see cref="GetConnectionAsync"/>. Registered as a DI singleton.
///
/// The initial connection attempt retries with exponential backoff (1s, 2s, 4s, 8s)
/// so the worker survives RabbitMQ still starting up under docker-compose instead of
/// crash-looping the container on the very first failed attempt. Once retries are
/// exhausted, the failure is allowed to propagate - see <see cref="MailQueueConsumerWorker"/>
/// and <c>Program.cs</c> (<c>BackgroundServiceExceptionBehavior.StopHost</c>) for why
/// a loud, logged failure is preferred over a silently-dead worker (rulebook Appendix
/// A.1: "worker ne smije tiho postati nedostupan bez logiranja razloga").
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqOptions options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check: another caller may have connected while we waited for the lock.
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.User,
                Password = _options.Password
            };

            await RetryHelper.RunWithRetryAsync(
                action: async () => _connection = await factory.CreateConnectionAsync(cancellationToken),
                onRetry: (ex, attempt, delay) => _logger.LogWarning(ex,
                    "Failed to connect to RabbitMQ at {Host}:{Port} (attempt {Attempt}/{Max}). Retrying in {Delay}...",
                    _options.Host, _options.Port, attempt, RetryHelper.DefaultBackoffDelays.Length, delay),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}.", _options.Host, _options.Port);
            return _connection!;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _connectLock.Dispose();
    }
}
