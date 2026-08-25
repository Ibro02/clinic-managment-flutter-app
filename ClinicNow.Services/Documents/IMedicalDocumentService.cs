using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Documents;

/// <summary>
/// Ownership here can't be expressed as a synchronous `ApplyFilter` (it needs an
/// async lookup of "which Patient row belongs to this JWT" for the Patient
/// role) - same reasoning as <c>IAppointmentService</c> - so this doesn't fit
/// the generic <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> shape
/// either (there's also no client-facing Update - a document is replaced by a
/// new upload, never edited in place).
/// </summary>
public interface IMedicalDocumentService
{
    Task<PagedResult<MedicalDocumentDto>> GetPagedAsync(MedicalDocumentSearchObject search, CancellationToken cancellationToken = default);

    Task<MedicalDocumentDto> UploadAsync(MedicalDocumentInsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Loads the raw file for download, enforcing the same ownership rule as the list. Never returns the tracked entity's bytes without that check.</summary>
    Task<MedicalDocument> GetFileForDownloadAsync(int id, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
