using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Records;

/// <summary>
/// The medical file ("medicinski karton") - one per patient, always addressed
/// by <c>patientId</c> (never its own free-standing ID from a client, since
/// "every patient has exactly one" makes the patient the natural key for
/// every write action except entry edit/delete, which need the entry's own ID).
/// </summary>
public interface IMedicalRecordService
{
    /// <summary>Ownership-checked: Administrator/Staff/Doctor see any patient's record; a Patient only their own.</summary>
    Task<MedicalRecordDto> GetByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>Doctor-only: appends (never replaces) new text onto Allergies/MedicalNotes.</summary>
    Task<MedicalRecordDto> AppendNotesAsync(int patientId, MedicalRecordAppendNotesRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrator-only: full replace of Allergies/MedicalNotes (real CRUD update).</summary>
    Task<MedicalRecordDto> ReplaceNotesAsync(int patientId, MedicalRecordUpdateNotesRequest request, CancellationToken cancellationToken = default);

    /// <summary>Doctor or Administrator: adds one row to the treatment history table.</summary>
    Task<MedicalRecordDto> AddEntryAsync(int patientId, MedicalRecordEntryInsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrator-only: edits an existing treatment-history row.</summary>
    Task<MedicalRecordDto> UpdateEntryAsync(int entryId, MedicalRecordEntryUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrator-only: removes a treatment-history row.</summary>
    Task DeleteEntryAsync(int entryId, CancellationToken cancellationToken = default);
}
