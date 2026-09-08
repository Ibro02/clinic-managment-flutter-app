using ClinicNow.Model.Localization;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// An account in the system. A single <c>User</c> row can carry multiple
/// <see cref="Role"/>s via <see cref="UserRole"/> (an admin acting as staff, for
/// instance), though in practice each seeded demo account has exactly one.
///
/// Passwords are never stored in plain text - <see cref="PasswordHash"/> is a BCrypt
/// hash (salt embedded in the hash string itself, per rulebook Part II §F). Deletion
/// is deliberately not modeled yet (Phase 1 scope is register/login/roles only); a
/// disabled account uses <see cref="IsActive"/> instead of being removed.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this account wants the pre-appointment reminder by email
    /// (review item C7). Real persisted state behind the mobile settings
    /// toggle - the item explicitly rules out a switch that changes nothing.
    /// Gates the email only: the in-app notification is how the clinic reaches
    /// a patient about their own appointment, and is not opt-out.
    /// </summary>
    public bool EmailRemindersEnabled { get; set; } = true;

    /// <summary>
    /// The mobile app's "jezik aplikacije" preference (review item 7 - kept from
    /// the submitted topic proposal, so it needs a real effect): <c>"bs"</c> or
    /// <c>"en"</c>, see <see cref="ClinicNow.Model.Localization.PatientLanguage"/>.
    /// Drives the language of every notification/email this account subsequently
    /// receives (<see cref="ClinicNow.Model.Localization.PatientMessages"/>) -
    /// only ever read for a Patient account, since Doctor/Staff/Administrator
    /// have no settings screen that sets it.
    /// </summary>
    public string PreferredLanguage { get; set; } = PatientLanguage.Bosnian;

    /// <summary>
    /// BCrypt hash of the current password-reset code, or null when no reset is
    /// pending (rulebook Part II §F: reset tokens are never stored in plain
    /// text and always expire). Cleared the moment a reset succeeds, which is
    /// what makes a code single-use.
    /// </summary>
    public string? PasswordResetTokenHash { get; set; }

    public DateTime? PasswordResetTokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Access tokens issued before this instant are rejected, regardless of their
    /// own expiry. Stamped whenever the password changes - by the owner or through
    /// a reset - so that changing the password actually ends every other session.
    ///
    /// The <see cref="RevokedToken"/> blocklist alone cannot do this: it is keyed by
    /// <c>jti</c> and only ever populated by an explicit logout, so before this
    /// existed an attacker's stolen token kept working right through the password
    /// reset the victim performed to lock them out. Null means "no cutoff", the
    /// state of every account that has never changed its password.
    /// </summary>
    public DateTime? TokensValidFromUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];

    /// <summary>
    /// Stamps <see cref="TokensValidFromUtc"/> to now, rejecting every access token
    /// issued before this call regardless of its own expiry - the one place this
    /// cutoff is computed, so a password change (<c>UserService</c>) and archiving
    /// a <c>Patient</c>/<c>Doctor</c>'s linked account can't drift apart on it.
    ///
    /// Truncated to whole seconds on purpose. A JWT's <c>iat</c> claim has one-second
    /// resolution, so an untruncated cutoff of 12:00:00.500 would be *after* the
    /// <c>iat</c> of a token minted in that same second (12:00:00) and could reject
    /// a token issued moments later in the same request. The cost is a sub-second
    /// window in which an older token from the same second survives, which is the
    /// standard trade for second-resolution claims.
    /// </summary>
    public void RevokeOutstandingTokens()
    {
        var now = DateTime.UtcNow;
        TokensValidFromUtc = new DateTime(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
    }
}
