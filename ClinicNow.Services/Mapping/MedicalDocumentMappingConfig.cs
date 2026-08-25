using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class MedicalDocumentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<MedicalDocument, MedicalDocumentDto>()
            .Map(dest => dest.PatientName, src => $"{src.Patient.FirstName} {src.Patient.LastName}")
            .Map(dest => dest.UploadedByName, src => $"{src.UploadedByUser.FirstName} {src.UploadedByUser.LastName}")
            .Map(dest => dest.DownloadUrl, src => $"/api/MedicalDocument/{src.Id}/download");
    }
}
