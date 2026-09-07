using ClinicNow.Model.Messaging;

namespace ClinicNow.Worker.Push;

/// <summary>
/// Delivers a single <see cref="PushMessage"/> to its devices. The push twin of
/// <see cref="Mail.IMailSender"/> - a real network call, no stub implementation.
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// Sends to every token on the message. Throws when the send genuinely
    /// failed and is worth retrying (network, 5xx, auth); returns normally when
    /// FCM rejected individual tokens as permanently invalid, since retrying
    /// those would never succeed.
    /// </summary>
    Task SendAsync(PushMessage message, CancellationToken cancellationToken);
}
