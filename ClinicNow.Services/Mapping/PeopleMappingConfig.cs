using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Explicit mapping rules Mapster's convention can't infer for the Phase 3
/// people entities - denormalizing linked <see cref="User"/> profile fields onto
/// <see cref="DoctorDto"/>, and doctor names onto <see cref="WorkingHoursDto"/>/
/// <see cref="ScheduleBlockDto"/> (rulebook Part II §K: never show raw IDs).
/// The relevant navigations (<c>Doctor.User</c>, <c>DoctorSpecializations</c>,
/// <c>WorkingHours.Doctor.User</c>, <c>ScheduleBlock.Doctor.User</c>) must be
/// loaded via <c>Include</c> for these to populate - see the corresponding services.
/// </summary>
public class PeopleMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // MedicalRecord is only populated when explicitly Include()-d (see
        // PatientService.ApplyFilter/GetByIdAsync) - null-safe here so mapping
        // a Patient without that Include still succeeds instead of throwing.
        config.NewConfig<Patient, PatientDto>()
            .Map(dest => dest.MedicalRecordId, src => src.MedicalRecord != null ? src.MedicalRecord.Id : (int?)null);

        config.NewConfig<Doctor, DoctorDto>()
            .Map(dest => dest.FirstName, src => src.User.FirstName)
            .Map(dest => dest.LastName, src => src.User.LastName)
            .Map(dest => dest.Email, src => src.User.Email)
            .Map(dest => dest.PhoneNumber, src => src.User.PhoneNumber)
            .Map(dest => dest.LocationName, src => src.Location.Name)
            .Map(dest => dest.Specializations, src => src.DoctorSpecializations.Select(ds => ds.Specialization.Name).ToList())
            .Map(dest => dest.SpecializationIds, src => src.DoctorSpecializations.Select(ds => ds.SpecializationId).ToList());

        config.NewConfig<WorkingHours, WorkingHoursDto>()
            .Map(dest => dest.DoctorName, src => src.Doctor.User.FirstName + " " + src.Doctor.User.LastName);

        config.NewConfig<ScheduleBlock, ScheduleBlockDto>()
            .Map(dest => dest.DoctorName, src => src.Doctor.User.FirstName + " " + src.Doctor.User.LastName);
    }
}
