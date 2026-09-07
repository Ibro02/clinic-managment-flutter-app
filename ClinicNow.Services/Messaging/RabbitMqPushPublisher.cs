using System.Text;
using System.Text.Json;
using ClinicNow.Model.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// Serializes a <see cref="PushMessage"/> as JSON and publishes it onto the
/// durable <c>push_sending</c> queue via the same shared singleton connection
/// the mail publisher uses (rulebook Appendix A.1: never a new connection per
/// publish).
/// </summary>
public class RabbitMqPushPublisher : IPushPublisher
{
    private const string PushQueueName = "push_sending";

    private readonly RabbitMqPublisherConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqPushPublisher> _logger;

    public RabbitMqPushPublisher(RabbitMqPublisherConnectionProvider connectionProvider, ILogger<RabbitMqPushPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    public async Task<bool> PublishAsync(PushMessage message, CancellationToken cancellationToken)
    {
        // Nothing to deliver to. Returning early keeps an empty message off the
        // queue entirely rather than making the Worker discover it has no
        // recipients - the common case for any user who has never opened the
        // mobile app.
        if (message.Tokens.Count == 0)
        {
            return true;
        }

        try
        {
            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

            // Publisher confirmations, same as the mail path - a publish that
            // returns before the broker has accepted the message tells us
            // nothing worth logging.
            await using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            await channel.QueueDeclareAsync(
                queue: PushQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var properties = new BasicProperties { Persistent = true };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: PushQueueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // Same rule as the mail publisher: a push that cannot be queued must
            // never break the operation that caused it. Logged, never swallowed.
            _logger.LogError(ex,
                "Failed to publish push for notification {NotificationId} to {DeviceCount} device(s).",
                message.NotificationId, message.Tokens.Count);
            return false;
        }
    }
}
