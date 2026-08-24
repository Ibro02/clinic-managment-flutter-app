using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Database;

/// <summary>
/// The single EF Core DbContext for ClinicNow - one database shared by the API and
/// the Worker. Domain <c>DbSet&lt;T&gt;</c> properties are added incrementally per
/// implementation phase (see PLAN.md, starting with identity in Phase 1); this class
/// currently holds only the cross-cutting plumbing so the migration/connectivity
/// pipeline can be proven end-to-end in Phase 0 before any domain entity exists:
///
/// <list type="bullet">
/// <item>entity configurations are discovered automatically via <see cref="IEntityTypeConfiguration{TEntity}"/>,</item>
/// <item>every <see cref="ISoftDelete"/> entity automatically gets a global query
/// filter excluding deleted rows, so no individual service can forget it.</item>
/// </list>
///
/// Registered as <c>Scoped</c> in DI (never <c>Transient</c>/<c>Singleton</c>) - see
/// rulebook Part II §D. All entity services share the one DbContext instance for a
/// given request, so cross-service calls never need their own SaveChanges.
/// </summary>
public class ClinicNowContext : DbContext
{
    public ClinicNowContext(DbContextOptions<ClinicNowContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // Builds: e => !e.IsDeleted  (per concrete entity CLR type)
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
            var notDeleted = Expression.Not(property);
            var lambda = Expression.Lambda(notDeleted, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
