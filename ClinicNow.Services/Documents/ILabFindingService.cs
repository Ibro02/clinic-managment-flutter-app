using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Documents;

/// <summary>
/// Ownership can't be expressed as a synchronous <c>ApplyFilter</c> (it needs
/// an async lookup of "which Patient row belongs to this JWT" for the Patient
/// role), and there's no client-facing Update - a finding is entered once,
/// never edited in place - so this doesn't fit the generic
/// <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> shape, same
/// reasoning as <see cref="IMedicalDocumentService"/>.
/// </summary>
public interface ILabFindingService
{
    Task<PagedResult<LabFindingDto>> GetPagedAsync(LabFindingSearchObject search, CancellationToken cancellationToken = default);

    Task<LabFindingDto> CreateAsync(LabFindingInsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Loads the raw file for download, enforcing the same ownership rule as the list. Never returns the tracked entity's bytes without that check.</summary>
    Task<LabFinding> GetFileForDownloadAsync(int id, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
