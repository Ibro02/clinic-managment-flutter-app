using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Denormalizes <see cref="MedicalRecord.Patient"/>'s basic info onto the DTO
/// header (rulebook Part II §K) and computes age from <c>DateOfBirth</c> -
/// requires <c>Patient</c> to be <c>Include</c>-d, see <c>MedicalRecordService</c>.
/// </summary>
public class MedicalRecordMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<MedicalRecord, MedicalRecordDto>()
            .Map(dest => dest.PatientFirstName, src => src.Patient.FirstName)
            .Map(dest => dest.PatientLastName, src => src.Patient.LastName)
            .Map(dest => dest.PatientAge, src => CalculateAge(src.Patient.DateOfBirth))
            .Map(dest => dest.PatientGender, src => src.Patient.Gender)
            .Map(dest => dest.PatientAddress, src => src.Patient.Address)
            .Map(dest => dest.PatientEmail, src => src.Patient.Email)
            .Map(dest => dest.PatientPhoneNumber, src => src.Patient.PhoneNumber)
            .Map(dest => dest.Entries, src => src.Entries.OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAtUtc));

        config.NewConfig<MedicalRecordEntry, MedicalRecordEntryDto>()
            .Map(dest => dest.CreatedByName, src => $"{src.CreatedByUser.FirstName} {src.CreatedByUser.LastName}");
    }

    private static int? CalculateAge(DateOnly? dateOfBirth)
    {
        if (dateOfBirth is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }
}
