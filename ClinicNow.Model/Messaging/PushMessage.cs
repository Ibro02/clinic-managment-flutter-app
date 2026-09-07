namespace ClinicNow.Model.Messaging;

/// <summary>
/// Shared publish/consume DTO for the <c>push_sending</c> RabbitMQ queue - the
/// mobile push counterpart of <see cref="EmailMessage"/>, referenced from
/// ClinicNow.Model so the API (publisher) and the Worker (consumer) agree on the
/// contract without either hand-rolling JSON.
///
/// The tokens are resolved by the API at publish time rather than looked up by
/// the Worker: the Worker deliberately has no database reference (it is a pure
/// side-effect service, exactly like the mail path), and resolving them at
/// publish time also means a device that unregisters a second later simply
/// receives a push FCM then discards, which is harmless.
/// </summary>
public class PushMessage
{
    /// <summary>FCM registration tokens to deliver to - one per device.</summary>
    public List<string> Tokens { get; set; } = [];

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// The persisted notification this push mirrors. Sent to the device as a
    /// data field so a tapped notification can deep-link to the right row, and
    /// logged by the Worker so a delivery can be traced back to what caused it.
    /// </summary>
    public int NotificationId { get; set; }
}
