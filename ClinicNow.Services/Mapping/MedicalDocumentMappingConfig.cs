using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class MedicalDocumentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<MedicalDocument, MedicalDocumentDto>()
            // Patient comes back null once the patient has been archived
            // (soft-deleted, review item C3) - the document itself is never
            // deleted, so it must stay readable rather than throwing a
            // NullReferenceException, same reasoning as AppointmentMappingConfig.
            .Map(dest => dest.PatientName, src => src.Patient == null ? "Obrisani pacijent" : $"{src.Patient.FirstName} {src.Patient.LastName}")
            .Map(dest => dest.UploadedByName, src => $"{src.UploadedByUser.FirstName} {src.UploadedByUser.LastName}")
            .Map(dest => dest.DownloadUrl, src => $"/api/MedicalDocument/{src.Id}/download");
    }
}
