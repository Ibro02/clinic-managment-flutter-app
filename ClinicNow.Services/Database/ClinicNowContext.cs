using System.Linq.Expressions;
using System.Reflection;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    public DbSet<City> Cities => Set<City>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<MedicalService> MedicalServices => Set<MedicalService>();

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSpecialization> DoctorSpecializations => Set<DoctorSpecialization>();
    public DbSet<WorkingHours> WorkingHoursEntries => Set<WorkingHours>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();

    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentAuditLog> AppointmentAuditLogs => Set<AppointmentAuditLog>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();

    public DbSet<MedicalDocument> MedicalDocuments => Set<MedicalDocument>();

    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalRecordEntry> MedicalRecordEntries => Set<MedicalRecordEntry>();

    public DbSet<RecommenderInteraction> RecommenderInteractions => Set<RecommenderInteraction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Root-cause fix for a whole class of client-side bugs, not a per-DTO
        // patch: every DateTime column in this schema is UTC by convention
        // (CLAUDE.md "Time Handling" - always named with a "Utc" suffix), but
        // SQL Server's `datetime2` has no timezone concept, so EF Core reads
        // every value back with Kind=Unspecified. System.Text.Json then
        // serializes those as e.g. "2026-09-01T00:00:00" - no "Z" - which a
        // client can silently misinterpret as *local* time. Forcing
        // Kind=Utc on read (write is a no-op; every value is already produced
        // via DateTime.UtcNow) makes the API's JSON honest about what it
        // actually is, for every entity, without each service remembering to
        // do it individually.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            toProvider => toProvider,
            fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            toProvider => toProvider,
            fromProvider => fromProvider.HasValue ? DateTime.SpecifyKind(fromProvider.Value, DateTimeKind.Utc) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }

            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // Builds: e => !e.IsDeleted  (per concrete entity CLR type)
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property2 = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
            var notDeleted = Expression.Not(property2);
            var lambda = Expression.Lambda(notDeleted, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
