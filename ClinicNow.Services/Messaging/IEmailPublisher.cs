using ClinicNow.Model.Messaging;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// Publishes an <see cref="EmailMessage"/> onto the <c>mail_sending</c> queue for
/// ClinicNow.Worker to actually send. The API never sends email itself - slow
/// side-effect work stays off the request path (CLAUDE.md §9).
/// </summary>
public interface IEmailPublisher
{
    Task PublishAsync(EmailMessage message, CancellationToken cancellationToken);
}
