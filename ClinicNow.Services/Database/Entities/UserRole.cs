namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Pure M:N join between <see cref="User"/> and <see cref="Role"/> - no extra
/// attributes of its own, so per CLAUDE.md §6 this is a reference/join table and
/// does not count toward the ≥10 non-reference tables.
/// </summary>
public class UserRole
{
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;
}
