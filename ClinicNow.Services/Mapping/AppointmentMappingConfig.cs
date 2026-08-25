using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Denormalizes display names onto <see cref="AppointmentDto"/> (rulebook Part II
/// §K: never show raw IDs). <c>AllowedActions</c> is deliberately NOT set here -
/// computing it requires resolving the right state-machine class via DI, which a
/// static Mapster config can't do - <see cref="AppointmentService"/> fills it in
/// as a post-mapping step instead.
/// </summary>
public class AppointmentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Appointment, AppointmentDto>()
            .Map(dest => dest.PatientName, src => src.Patient.FirstName + " " + src.Patient.LastName)
            .Map(dest => dest.DoctorName, src => src.Doctor.User.FirstName + " " + src.Doctor.User.LastName)
            .Map(dest => dest.MedicalServiceName, src => src.MedicalService.Name)
            .Map(dest => dest.LocationName, src => src.Location.Name)
            .Map(dest => dest.StatusName, src => src.Status.ToDisplayName())
            .Ignore(dest => dest.AllowedActions);
    }
}
