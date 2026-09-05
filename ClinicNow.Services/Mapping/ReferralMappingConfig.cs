using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class ReferralMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Referral, ReferralDto>()
            // Patient comes back null once the patient has been archived
            // (soft-deleted, review item C3) - the referral itself is never
            // deleted, so it must stay readable rather than throwing a
            // NullReferenceException, same reasoning as LabFindingMappingConfig.
            .Map(dest => dest.PatientName, src => src.Patient == null ? "Obrisani pacijent" : $"{src.Patient.FirstName} {src.Patient.LastName}")
            .Map(dest => dest.ReferringDoctorName, src => $"{src.ReferringDoctor.User.FirstName} {src.ReferringDoctor.User.LastName}")
            .Map(dest => dest.SourceAppointmentStartUtc, src => src.SourceAppointment.StartUtc)
            .Map(dest => dest.TargetSpecializationName, src => src.TargetSpecialization.Name)
            .Map(dest => dest.IsUsed, src => src.ResultingAppointmentId != null);
    }
}
