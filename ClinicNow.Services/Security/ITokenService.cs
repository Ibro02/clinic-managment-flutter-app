using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Security;

/// <summary>Issues signed JWT access tokens.</summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a signed JWT for <paramref name="user"/> carrying one role claim per
    /// entry in <paramref name="roles"/> and a unique <c>jti</c> claim (used by
    /// <see cref="ITokenBlocklistService"/> to support server-side logout).
    /// </summary>
    CreatedToken CreateAccessToken(User user, IEnumerable<string> roles);
}

public record CreatedToken(string AccessToken, DateTime ExpiresAtUtc);
