using System.Text;
using System.Text.Json;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.Resilience;
using ClinicNow.Worker.Messaging;
using ClinicNow.Worker.Push;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ClinicNow.Worker;

/// <summary>
/// Consumes the <c>push_sending</c> queue and performs the real work: deliver
/// the notification to the user's devices through FCM
/// (<see cref="IPushSender"/>), retrying with exponential backoff.
///
/// Structurally identical to <see cref="MailQueueConsumerWorker"/> on purpose -
/// same <see cref="AsyncEventingBasicConsumer"/>, same shared singleton
/// connection, same ack-after-attempt policy, same "no dead-letter queue in this
/// scope, but never a silent failure" rule (rulebook Appendix A.1). Two
/// consumers in the same Worker container, not two containers: they share the
/// broker connection and neither is heavy enough to isolate.
/// </summary>
public class PushQueueConsumerWorker : BackgroundService
{
    private const string PushQueueName = "push_sending";

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushQueueConsumerWorker> _logger;

    public PushQueueConsumerWorker(
        RabbitMqConnectionProvider connectionProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<PushQueueConsumerWorker> logger)
    {
        _connectionProvider = connectionProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: PushQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            var raw = Encoding.UTF8.GetString(args.Body.ToArray());
            PushMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<PushMessage>(raw);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Discarding malformed push message: {Raw}", raw);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                return;
            }

            if (message is null)
            {
                _logger.LogError("Discarding null-deserialized push message: {Raw}", raw);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var pushSender = scope.ServiceProvider.GetRequiredService<IPushSender>();

            try
            {
                await RetryHelper.RunWithRetryAsync(
                    action: () => pushSender.SendAsync(message, stoppingToken),
                    onRetry: (ex, attempt, delay) => _logger.LogWarning(ex,
                        "Failed to push notification {NotificationId} (attempt {Attempt}/{Max}). Retrying in {Delay}...",
                        message.NotificationId, attempt, RetryHelper.DefaultBackoffDelays.Length, delay),
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Giving up pushing notification {NotificationId} after all retries. It is still stored and visible in-app.",
                    message.NotificationId);
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await channel.BasicConsumeAsync(
            queue: PushQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("ClinicNow.Worker is listening on '{Queue}'.", PushQueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
