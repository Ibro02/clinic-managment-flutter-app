using ClinicNow.Model.Configuration;
using ClinicNow.Model.Messaging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ClinicNow.Worker.Mail;

/// <summary>
/// Real SMTP delivery via MailKit (<see cref="SmtpOptions"/>, `.env`-configured -
/// never hardcoded). This performs a genuine network connection attempt every
/// time; if credentials aren't configured (empty dev sandbox), the send
/// legitimately fails and that failure is surfaced to the caller
/// (<see cref="MailQueueConsumerWorker"/>) for its retry/backoff loop to handle -
/// per "no quick fixes", this is not a silently-successful stub.
/// </summary>
public class MailSender : IMailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<MailSender> _logger;

    public MailSender(SmtpOptions options, ILogger<MailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(_options.User) ? "no-reply@clinicnow.test" : _options.User));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.EnableSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.User))
        {
            await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Sent email to {To} ({Subject}).", message.To, message.Subject);
    }
}
