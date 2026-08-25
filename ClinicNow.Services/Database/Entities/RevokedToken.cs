namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Records a JWT access token's <c>jti</c> claim once it has been explicitly logged
/// out, so that token is rejected server-side for the remainder of its natural
/// lifetime even though JWTs are otherwise stateless (rulebook §5: "Logout mora
/// nevalidirati token na serverskoj strani, ne samo lokalno").
///
/// <see cref="ExpiresAtUtc"/> mirrors the token's own <c>exp</c> claim purely so a
/// future cleanup job can safely purge rows whose underlying token would have
/// expired anyway - tracked as a follow-up, not required for Phase 1 correctness
/// since an unbounded table of revoked jtis does not, by itself, break anything.
/// </summary>
public class RevokedToken
{
    public int Id { get; set; }

    public string Jti { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime RevokedAtUtc { get; set; }
}
