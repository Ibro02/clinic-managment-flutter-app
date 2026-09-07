namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One push-notification registration token for one of a user's devices.
///
/// A row per device rather than a column on <see cref="User"/>, because a
/// patient with a phone and a tablet should be reachable on both - and because
/// FCM tokens are per-installation, not per-account: the same person
/// reinstalling the app produces a different token.
/// </summary>
public class DeviceToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// The FCM registration token. Unique across the table: a device that
    /// signs out and a different user signs in keeps its token, and that token
    /// must then belong to exactly one user - the new one - or the previous
    /// user's notifications would keep arriving on it.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Which client produced it, for diagnostics ("Android", "Windows").</summary>
    public string Platform { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Refreshed every time the device re-registers (each login, and on FCM
    /// token refresh), so a stale row is identifiable as stale.
    /// </summary>
    public DateTime LastSeenAtUtc { get; set; }
}
