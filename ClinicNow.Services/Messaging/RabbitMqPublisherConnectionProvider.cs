using ClinicNow.Model.Configuration;
using ClinicNow.Model.Resilience;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// API-side mirror of ClinicNow.Worker's RabbitMqConnectionProvider: a single
/// singleton <see cref="IConnection"/> for the lifetime of the API process, never a
/// new connection per publish (rulebook Appendix A.1). Kept as a separate type from
/// the Worker's provider since the two projects don't share a project reference.
/// </summary>
public sealed class RabbitMqPublisherConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisherConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqPublisherConnectionProvider(RabbitMqOptions options, ILogger<RabbitMqPublisherConnectionProvider> logger)
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

            _logger.LogInformation("API connected to RabbitMQ at {Host}:{Port}.", _options.Host, _options.Port);
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
