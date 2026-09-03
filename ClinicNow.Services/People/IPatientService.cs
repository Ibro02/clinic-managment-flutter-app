using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.People;

/// <summary>
/// Patient rides the generic CRUD shape but also needs one operation the
/// generic <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> has no
/// room for - undoing an archive. Same reasoning as <c>IAppointmentService</c>:
/// a bespoke interface on top of the generic one, rather than adding Restore
/// to <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> itself and
/// forcing every other entity to grow a method it doesn't use.
/// </summary>
public interface IPatientService : ICRUDService<PatientDto, PatientSearchObject, PatientInsertRequest, PatientUpdateRequest>
{
    /// <summary>
    /// Un-archives a soft-deleted patient and reactivates their linked login
    /// (the exact reverse of <c>PatientService.BeforeDeleteAsync</c>). Throws
    /// <see cref="Model.Exceptions.NotFoundException"/> if no patient with this
    /// id exists at all, or <see cref="Model.Exceptions.BusinessException"/> if
    /// it exists but isn't currently archived.
    /// </summary>
    Task<PatientDto> RestoreAsync(int id, CancellationToken cancellationToken = default);
}
