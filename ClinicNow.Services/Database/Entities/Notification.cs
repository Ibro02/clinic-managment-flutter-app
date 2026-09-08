namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// An in-app notification for a single user (booking/confirmation/cancellation/
/// status-change/payment events - rulebook Part II §G). Auto-refreshed on the
/// client by polling this list (<c>NotificationCenter</c> in both Flutter apps),
/// never requiring a manual refresh.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
