using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Denormalizes <see cref="MedicalRecord.Patient"/>'s basic info onto the DTO
/// header (rulebook Part II §K) and computes age from <c>DateOfBirth</c> -
/// requires <c>Patient</c> to be <c>Include</c>-d, see <c>MedicalRecordService</c>.
///
/// <c>Patient</c> is null-guarded throughout: it comes back null once the
/// patient has been archived (soft-deleted, review item C3) - the record
/// itself is never deleted, so the karton and its treatment history must stay
/// readable rather than throwing a NullReferenceException, the same reasoning
/// <see cref="AppointmentMappingConfig"/> already applies to
/// <c>Appointment.Patient</c>.
/// </summary>
public class MedicalRecordMappingConfig : IRegister
{
    private const string ArchivedPatientName = "Obrisani pacijent";

    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<MedicalRecord, MedicalRecordDto>()
            .Map(dest => dest.PatientFirstName, src => src.Patient == null ? ArchivedPatientName : src.Patient.FirstName)
            .Map(dest => dest.PatientLastName, src => src.Patient == null ? string.Empty : src.Patient.LastName)
            .Map(dest => dest.PatientAge, src => src.Patient == null ? null : CalculateAge(src.Patient.DateOfBirth))
            .Map(dest => dest.PatientGender, src => src.Patient == null ? null : src.Patient.Gender)
            .Map(dest => dest.PatientAddress, src => src.Patient == null ? null : src.Patient.Address)
            .Map(dest => dest.PatientEmail, src => src.Patient == null ? null : src.Patient.Email)
            .Map(dest => dest.PatientPhoneNumber, src => src.Patient == null ? null : src.Patient.PhoneNumber)
            .Map(dest => dest.Entries, src => src.Entries.OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAtUtc));

        config.NewConfig<MedicalRecordEntry, MedicalRecordEntryDto>()
            .Map(dest => dest.CreatedByName, src => $"{src.CreatedByUser.FirstName} {src.CreatedByUser.LastName}")
            // Requires Diagnosis to be Include-d (MedicalRecordService.LoadFullRecordAsync).
            // The DTO carries code and name separately so the client can render
            // "J06.9 - ..." without ever seeing the raw id (rulebook §6).
            .Map(dest => dest.DiagnosisCode, src => src.Diagnosis.Code)
            .Map(dest => dest.DiagnosisName, src => src.Diagnosis.Name)
            .Map(dest => dest.DiagnosisSpecializationId, src => src.Diagnosis.SuggestedSpecializationId);
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
