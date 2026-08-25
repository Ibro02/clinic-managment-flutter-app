using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Security;

/// <summary>
/// EF-backed <see cref="ITokenBlocklistService"/>. Scoped (shares the request's
/// <see cref="ClinicNowContext"/>) - never Transient/Singleton, per rulebook Part II §D.
/// </summary>
public class TokenBlocklistService : ITokenBlocklistService
{
    private readonly ClinicNowContext _context;

    public TokenBlocklistService(ClinicNowContext context)
    {
        _context = context;
    }

    public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
        _context.RevokedTokens.AnyAsync(rt => rt.Jti == jti, cancellationToken);

    public async Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        // Idempotent: logging out twice with the same token must not throw.
        if (await IsRevokedAsync(jti, cancellationToken))
        {
            return;
        }

        _context.RevokedTokens.Add(new RevokedToken
        {
            Jti = jti,
            ExpiresAtUtc = tokenExpiresAtUtc,
            RevokedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
