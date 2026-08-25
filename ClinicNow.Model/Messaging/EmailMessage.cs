namespace ClinicNow.Model.Messaging;

/// <summary>
/// Shared publish/consume DTO for the <c>mail_sending</c> RabbitMQ queue. The API
/// (publisher) and the Worker (consumer) both reference this type from
/// ClinicNow.Model so neither side has to hand-roll JSON contracts.
/// </summary>
public class EmailMessage
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
