using ClinicNow.Model.Common;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services;

/// <summary>
/// Read-only surface shared by every entity service: paged listing + get-by-id.
/// All members are async - the rulebook (Part II §D) requires async end-to-end
/// through the whole stack, with no <c>.Result</c>/<c>.Wait()</c> anywhere.
/// </summary>
public interface IService<TModel, in TSearch> where TSearch : BaseSearchObject
{
    Task<PagedResult<TModel>> GetPagedAsync(TSearch search, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>null</c> if no matching (non-deleted) row exists.</summary>
    Task<TModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
