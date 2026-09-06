using ClinicNow.Model.Messaging;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// Publishes an <see cref="EmailMessage"/> onto the <c>mail_sending</c> queue for
/// ClinicNow.Worker to actually send. The API never sends email itself - slow
/// side-effect work stays off the request path (CLAUDE.md §9).
/// </summary>
public interface IEmailPublisher
{
    /// <summary>
    /// Hands the message to the broker. Returns <c>true</c> only once RabbitMQ
    /// has actually confirmed it (review item C16) - never merely because the
    /// call didn't throw. Callers that record "reminder sent" state must key
    /// off this, or a broker outage gets written down as a delivery that
    /// happened and the message is lost for good.
    ///
    /// Still never throws: an email failure must not break the request that
    /// triggered it (confirming an appointment has to succeed even when
    /// RabbitMQ is briefly down), which is exactly why the outcome has to come
    /// back as a value instead.
    /// </summary>
    Task<bool> PublishAsync(EmailMessage message, CancellationToken cancellationToken);
}
