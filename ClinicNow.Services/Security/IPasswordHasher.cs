namespace ClinicNow.Services.Security;

/// <summary>
/// Password hashing abstraction, per rulebook Part II §F: BCrypt/Argon2/PBKDF2 with a
/// per-user salt only - never SHA-1/SHA-256-without-salt/HMAC/custom.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plain-text password. The returned string embeds its own salt.</summary>
    string Hash(string password);

    /// <summary>Verifies a plain-text password against a previously hashed value.</summary>
    bool Verify(string password, string passwordHash);
}
