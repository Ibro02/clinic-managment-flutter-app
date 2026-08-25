using ClinicNow.Model.Messaging;

namespace ClinicNow.Worker.Mail;

/// <summary>Sends a single email via real SMTP (MailKit). No stub/no-op implementation - see <see cref="MailSender"/>.</summary>
public interface IMailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
