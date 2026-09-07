namespace ClinicNow.Services.Security;

/// <summary>
/// Backs server-side JWT logout: a token whose <c>jti</c> has been revoked is
/// rejected on every subsequent request, even though it hasn't naturally expired yet
/// (rulebook §5).
/// </summary>
public interface ITokenBlocklistService
{
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);

    Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The instant before which this user's tokens are no longer accepted, or null
    /// if the account has no cutoff. Separate from the per-<c>jti</c> blocklist
    /// because a password change has to invalidate every outstanding token at once,
    /// including ones this process has never seen.
    /// </summary>
    Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached cutoff for a user. Must be called right after persisting a
    /// new <c>TokensValidFromUtc</c>: the cached value is what JWT validation reads,
    /// so without this a password change would not actually start rejecting old
    /// tokens until the cache entry aged out - exactly the window the cutoff exists
    /// to close.
    /// </summary>
    void InvalidateTokensValidFrom(int userId);

    /// <summary>
    /// Deletes blocklist rows whose underlying token has expired anyway. Without
    /// this the table only ever grows: every logout adds a row that stays relevant
    /// for at most the token lifetime but was being kept forever.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
