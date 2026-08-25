using ClinicNow.Model.Security;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(r => r.Name).IsRequired().HasMaxLength(50);
        builder.HasIndex(r => r.Name).IsUnique();

        // Fixed, well-known IDs so other seeded HasData rows (UserRole) can reference
        // them by a stable number, and so the names can never drift from
        // ClinicNow.Model.Security.Roles - the single source of truth for role
        // strings (rulebook §5).
        builder.HasData(
            new Role { Id = 1, Name = Roles.Administrator },
            new Role { Id = 2, Name = Roles.Staff },
            new Role { Id = 3, Name = Roles.Doctor },
            new Role { Id = 4, Name = Roles.Patient });
    }
}
