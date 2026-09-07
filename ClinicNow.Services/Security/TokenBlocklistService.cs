using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ClinicNow.Services.Security;

/// <summary>
/// EF-backed <see cref="ITokenBlocklistService"/>. Scoped (shares the request's
/// <see cref="ClinicNowContext"/>) - never Transient/Singleton, per rulebook Part II §D.
///
/// Both lookups here run inside JWT validation, so they execute on *every*
/// authenticated request - every list, every 20-second notification poll, every image
/// fetch. Uncached that is two database round-trips per request to answer questions
/// whose answer almost never changes, so both are memoized in <see cref="IMemoryCache"/>
/// (a singleton, shared across requests) with deliberately asymmetric lifetimes.
/// </summary>
public class TokenBlocklistService : ITokenBlocklistService
{
    /// <summary>
    /// How long a "this token is still valid" answer may be reused. This is the
    /// window in which an already-issued token keeps working after logout, so it is
    /// a real security/latency trade rather than a free optimization - short enough
    /// that a logged-out token dies promptly, long enough to absorb a poll loop.
    /// </summary>
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A revoked token never becomes un-revoked and a password-change cutoff only
    /// ever moves forward, so positive answers can be held far longer without ever
    /// being wrong in the permissive direction.
    /// </summary>
    private static readonly TimeSpan PositiveCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly ClinicNowContext _context;
    private readonly IMemoryCache _cache;

    public TokenBlocklistService(ClinicNowContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    private static string RevokedKey(string jti) => $"jwt:revoked:{jti}";

    private static string ValidFromKey(int userId) => $"jwt:validfrom:{userId}";

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(RevokedKey(jti), out bool cached))
        {
            return cached;
        }

        var revoked = await _context.RevokedTokens
            .AsNoTracking()
            .AnyAsync(rt => rt.Jti == jti, cancellationToken);

        _cache.Set(RevokedKey(jti), revoked, revoked ? PositiveCacheLifetime : NegativeCacheLifetime);
        return revoked;
    }

    public async Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(ValidFromKey(userId), out DateTime? cached))
        {
            return cached;
        }

        var validFrom = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TokensValidFromUtc)
            .SingleOrDefaultAsync(cancellationToken);

        _cache.Set(ValidFromKey(userId), validFrom,
            validFrom.HasValue ? PositiveCacheLifetime : NegativeCacheLifetime);
        return validFrom;
    }

    public void InvalidateTokensValidFrom(int userId) => _cache.Remove(ValidFromKey(userId));

    public async Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default)
    {
        // Idempotent: logging out twice with the same token must not throw.
        // Queried directly rather than through IsRevokedAsync so a cached negative
        // can't make a genuine second logout look like a first one.
        var alreadyRevoked = await _context.RevokedTokens
            .AsNoTracking()
            .AnyAsync(rt => rt.Jti == jti, cancellationToken);

        if (alreadyRevoked)
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

        // Promote immediately: the whole point of logout is that the very next
        // request fails, which a stale negative entry would otherwise defer by up
        // to NegativeCacheLifetime.
        _cache.Set(RevokedKey(jti), true, PositiveCacheLifetime);
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
        _context.RevokedTokens
            .Where(rt => rt.ExpiresAtUtc < DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);
}
