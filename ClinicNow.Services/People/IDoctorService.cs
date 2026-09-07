using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.People;

/// <summary>
/// Doctor rides the generic CRUD shape but needs one operation it has no room
/// for: reading your own doctor profile without knowing your doctor id. Same
/// bespoke-interface-on-top-of-the-generic-one shape as
/// <see cref="IPatientService"/>, rather than widening
/// <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> for every entity.
/// </summary>
public interface IDoctorService : ICRUDService<DoctorDto, DoctorSearchObject, DoctorInsertRequest, DoctorUpdateRequest>
{
    /// <summary>
    /// The caller's own doctor profile, resolved from the JWT rather than a
    /// route or body value (rulebook Part II §F). This is what lets the desktop
    /// profile screen show a doctor their clinic, specializations and licence
    /// as read-only detail: those are Administrator/Staff-owned fields, so the
    /// doctor needs to *see* them without being able to reach an update
    /// endpoint for them.
    ///
    /// Throws <see cref="Model.Exceptions.NotFoundException"/> when the
    /// signed-in user has no doctor profile.
    /// </summary>
    Task<DoctorDto> GetOwnAsync(CancellationToken cancellationToken = default);
}
