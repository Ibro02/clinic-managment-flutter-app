using System.Text;
using System.Text.Json;
using ClinicNow.Model.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// Serializes an <see cref="EmailMessage"/> as JSON and publishes it onto the
/// durable <c>mail_sending</c> queue via the shared singleton connection.
/// </summary>
public class RabbitMqEmailPublisher : IEmailPublisher
{
    private const string MailQueueName = "mail_sending";

    private readonly RabbitMqPublisherConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqEmailPublisher> _logger;

    public RabbitMqEmailPublisher(RabbitMqPublisherConnectionProvider connectionProvider, ILogger<RabbitMqEmailPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    public async Task PublishAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: MailQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var properties = new BasicProperties { Persistent = true };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: MailQueueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let an email-publish failure break the request (e.g. an
            // appointment confirmation must still succeed even if RabbitMQ is
            // briefly unavailable) - log loudly instead of a silent catch
            // (rulebook Appendix A.1: no silent failures).
            _logger.LogError(ex, "Failed to publish email to '{To}'.", message.To);
        }
    }
}
