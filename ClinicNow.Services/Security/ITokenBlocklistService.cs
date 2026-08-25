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
}
