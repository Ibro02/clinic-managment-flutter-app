using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// <c>Location -&gt; LocationDto</c> needs one explicit rule Mapster's convention
/// can't infer: denormalizing <c>City.Name</c> onto the DTO for display (rulebook
/// Part II §K: never show raw IDs). <see cref="Database.Entities.Location.City"/>
/// must be loaded (via <c>Include</c>) for this to populate - see
/// <c>LocationService</c>.
/// </summary>
public class CodebookMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Location, LocationDto>()
            .Map(dest => dest.CityName, src => src.City != null ? src.City.Name : string.Empty);

        config.NewConfig<MedicalService, MedicalServiceDto>()
            .Map(dest => dest.SpecializationName, src => src.Specialization != null ? src.Specialization.Name : string.Empty);
    }
}
