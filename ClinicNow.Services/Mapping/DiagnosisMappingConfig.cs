using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Resolves the suggested specialization to its name so the codebook table and
/// the diagnosis dropdown never render a raw id (rulebook §6). Requires
/// <c>SuggestedSpecialization</c> to be <c>Include</c>-d - see
/// <c>DiagnosisService.ApplyFilter</c>.
/// </summary>
public class DiagnosisMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Diagnosis, DiagnosisDto>()
            .Map(dest => dest.SuggestedSpecializationName,
                src => src.SuggestedSpecialization == null ? null : src.SuggestedSpecialization.Name);
    }
}
