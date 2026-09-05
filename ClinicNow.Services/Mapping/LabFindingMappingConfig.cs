using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class LabFindingMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<LabFinding, LabFindingDto>()
            // Patient comes back null once the patient has been archived
            // (soft-deleted, review item C3) - the finding itself is never
            // deleted, so it must stay readable rather than throwing a
            // NullReferenceException, same reasoning as MedicalDocumentMappingConfig.
            .Map(dest => dest.PatientName, src => src.Patient == null ? "Obrisani pacijent" : $"{src.Patient.FirstName} {src.Patient.LastName}")
            .Map(dest => dest.AppointmentStartUtc, src => src.Appointment.StartUtc)
            .Map(dest => dest.MedicalServiceName, src => src.Appointment.MedicalService.Name)
            .Map(dest => dest.EnteredByName, src => $"{src.EnteredByUser.FirstName} {src.EnteredByUser.LastName}")
            .Map(dest => dest.DownloadUrl, src => $"/api/LabFinding/{src.Id}/download");
    }
}
