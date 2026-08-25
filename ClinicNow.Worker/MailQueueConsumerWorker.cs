using System.Text;
using System.Text.Json;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.Resilience;
using ClinicNow.Worker.Mail;
using ClinicNow.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ClinicNow.Worker;

/// <summary>
/// Consumes the <c>mail_sending</c> queue and performs the real work: deserialize
/// into an <see cref="EmailMessage"/>, send it via MailKit/SMTP
/// (<see cref="IMailSender"/>), retrying with exponential backoff on failure
/// (rulebook Appendix A.1). Uses <see cref="AsyncEventingBasicConsumer"/> (never
/// the deprecated synchronous variant) over the shared singleton RabbitMQ
/// connection (<see cref="RabbitMqConnectionProvider"/>).
///
/// A message is ACKed after the attempt regardless of final outcome - there's no
/// dead-letter-queue infrastructure in this scope (documented simplification, not
/// a silent failure: every failed attempt, retry, and final give-up is logged via
/// <see cref="ILogger{TCategoryName}"/>, never swallowed).
/// </summary>
public class MailQueueConsumerWorker : BackgroundService
{
    private const string MailQueueName = "mail_sending";

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailQueueConsumerWorker> _logger;

    public MailQueueConsumerWorker(
        RabbitMqConnectionProvider connectionProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<MailQueueConsumerWorker> logger)
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
            queue: MailQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            var raw = Encoding.UTF8.GetString(args.Body.ToArray());
            EmailMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<EmailMessage>(raw);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Discarding malformed mail message: {Raw}", raw);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                return;
            }

            if (message is null)
            {
                _logger.LogError("Discarding null-deserialized mail message: {Raw}", raw);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var mailSender = scope.ServiceProvider.GetRequiredService<IMailSender>();

            try
            {
                await RetryHelper.RunWithRetryAsync(
                    action: () => mailSender.SendAsync(message, stoppingToken),
                    onRetry: (ex, attempt, delay) => _logger.LogWarning(ex,
                        "Failed to send email to {To} (attempt {Attempt}/{Max}). Retrying in {Delay}...",
                        message.To, attempt, RetryHelper.DefaultBackoffDelays.Length, delay),
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                // Retries exhausted - log loudly and move on (no dead-letter queue
                // in this scope, but never a silent catch - rulebook Appendix A.1).
                _logger.LogError(ex, "Giving up sending email to {To} after all retries.", message.To);
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await channel.BasicConsumeAsync(
            queue: MailQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("ClinicNow.Worker is listening on '{Queue}'.", MailQueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
