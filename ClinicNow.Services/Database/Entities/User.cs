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

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
}
