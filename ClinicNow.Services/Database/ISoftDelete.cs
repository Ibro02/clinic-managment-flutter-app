namespace ClinicNow.Services.Database;

/// <summary>
/// Marker interface for entities that must never be physically removed - primarily
/// health-record-adjacent data (<c>Patient</c>, <c>Appointment</c>, medical documents)
/// per legal retention requirements (CLAUDE.md §5/§6).
///
/// <see cref="Services.BaseCRUDService{TModel,TSearch,TDbEntity,TInsert,TUpdate}.DeleteAsync"/>
/// flips <see cref="IsDeleted"/> instead of issuing a DELETE, and
/// <see cref="ClinicNowContext"/> applies a global query filter so soft-deleted rows
/// never leak into normal queries. Soft-delete purge (if ever implemented) must
/// respect FK order - children before parents (rulebook Part II §A).
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
}
