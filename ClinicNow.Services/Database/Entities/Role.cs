namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A reference table (codebook) of the fixed system roles. Row names must match the
/// string constants in <see cref="ClinicNow.Model.Security.Roles"/> exactly - that's
/// what <c>[Authorize(Roles = ...)]</c> attributes compare against, and what the JWT
/// role claims carry (rulebook §5: "seed role names must match the attribute
/// strings"). Not counted toward the ≥10 non-reference tables (CLAUDE.md §6).
/// </summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; set; } = [];
}
