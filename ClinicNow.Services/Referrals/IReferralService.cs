using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.Referrals;

/// <summary>
/// Ownership can't be expressed as a synchronous <c>ApplyFilter</c> (it needs
/// an async lookup of "which Patient row belongs to this JWT" for the Patient
/// role), and there is deliberately no Update/Delete - a referral "stays part
/// of the medical history" (review item C5) - so this doesn't fit the generic
/// <see cref="ICRUDService{TModel,TSearch,TInsert,TUpdate}"/> shape, same
/// reasoning as <see cref="ClinicNow.Services.Documents.ILabFindingService"/>.
/// </summary>
public interface IReferralService
{
    Task<PagedResult<ReferralDto>> GetPagedAsync(ReferralSearchObject search, CancellationToken cancellationToken = default);

    Task<ReferralDto> CreateAsync(ReferralInsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrator-only override to remove a mistaken referral - soft-delete, never a physical removal.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
