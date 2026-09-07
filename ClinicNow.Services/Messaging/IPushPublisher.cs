using ClinicNow.Model.Messaging;

namespace ClinicNow.Services.Messaging;

/// <summary>
/// Publishes a device push onto the broker for the Worker to deliver. The
/// mobile-push twin of <see cref="IEmailPublisher"/>, and deliberately the same
/// shape: the API never talks to FCM itself, so a slow or unreachable Google
/// endpoint can never sit inside a booking request.
/// </summary>
public interface IPushPublisher
{
    /// <summary>
    /// Returns whether the broker acknowledged the message. Callers treat a
    /// false as a logged miss rather than a failure - unlike the pre-appointment
    /// reminder (review item C16), nothing records a push as "sent", so there is
    /// no state to get wrong.
    /// </summary>
    Task<bool> PublishAsync(PushMessage message, CancellationToken cancellationToken);
}
