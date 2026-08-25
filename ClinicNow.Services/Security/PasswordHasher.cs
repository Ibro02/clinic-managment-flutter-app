namespace ClinicNow.Services.Security;

/// <summary>
/// BCrypt-backed <see cref="IPasswordHasher"/>. Work factor 12 is BCrypt.Net-Next's
/// own recommended default for interactive login as of 2026 hardware - high enough
/// to resist brute force, low enough not to noticeably slow down a login request.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
