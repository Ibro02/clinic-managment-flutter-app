using System.Text;
using ClinicNow.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ClinicNow.Worker;

/// <summary>
/// Phase 0 skeleton for the async email-notification worker described in CLAUDE.md
/// §9. It proves the worker container starts, obtains the shared singleton RabbitMQ
/// connection via <see cref="RabbitMqConnectionProvider"/>, declares the
/// <c>mail_sending</c> queue, and attaches an <see cref="AsyncEventingBasicConsumer"/>
/// (never the deprecated synchronous <c>EventingBasicConsumer</c> - rulebook
/// Appendix A.1).
///
/// Real message handling - deserializing a mail DTO, sending via MailKit/SMTP, and
/// per-message retry with exponential backoff on failure - is Phase 5 scope. For now
/// this only logs what it receives and acknowledges the message.
/// </summary>
public class MailQueueConsumerWorker : BackgroundService
{
    private const string MailQueueName = "mail_sending";

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<MailQueueConsumerWorker> _logger;

    public MailQueueConsumerWorker(RabbitMqConnectionProvider connectionProvider, ILogger<MailQueueConsumerWorker> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: MailQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            var message = Encoding.UTF8.GetString(args.Body.ToArray());
            _logger.LogInformation("Received message on '{Queue}': {Message}", MailQueueName, message);

            // TODO(Phase 5): deserialize into a mail DTO, send via MailKit/SMTP
            // (SmtpOptions is already wired in Program.cs), and retry with
            // exponential backoff on failure instead of unconditionally ACK-ing.
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await channel.BasicConsumeAsync(
            queue: MailQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("ClinicNow.Worker is listening on '{Queue}'.", MailQueueName);

        // RabbitMQ.Client dispatches ReceivedAsync callbacks on its own I/O thread;
        // this just keeps the channel/host alive until shutdown is requested.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
