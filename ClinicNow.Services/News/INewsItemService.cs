using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.News;

/// <summary>
/// News items ride the generic CRUD shape but need one operation it has no room
/// for - serving the raw image bytes, which are deliberately kept off the
/// list/detail DTO (rulebook Part II §D: no heavy blobs in list responses).
/// A bespoke interface on top of the generic one, same reasoning as
/// <c>IPatientService</c>.
/// </summary>
public interface INewsItemService : ICRUDService<NewsItemDto, NewsItemSearchObject, NewsItemInsertRequest, NewsItemUpdateRequest>
{
    /// <summary>
    /// The stored image for one news item, or null when the item exists but
    /// carries no image. Throws <see cref="Model.Exceptions.NotFoundException"/>
    /// when there is no such news item at all - the caller turns that into a
    /// 404, and cannot tell the two apart, which is fine for public content.
    /// </summary>
    Task<(byte[] Data, string ContentType)?> GetImageAsync(int id, CancellationToken cancellationToken = default);
}
