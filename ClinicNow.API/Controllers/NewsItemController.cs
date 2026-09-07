using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// News/announcements. Reads open to any authenticated role (desktop staff and
/// mobile patients both browse this); writes Administrator/Staff only.
/// </summary>
public class NewsItemController : BaseCRUDController<NewsItemDto, NewsItemSearchObject, NewsItemInsertRequest, NewsItemUpdateRequest>
{
    private readonly INewsItemService _service;

    public NewsItemController(INewsItemService service) : base(service)
    {
        _service = service;
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<NewsItemDto>> Insert(NewsItemInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<NewsItemDto>> Update(int id, NewsItemUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);

    /// <summary>Serves the raw image bytes for a news item - kept off the list/detail DTO (rulebook Part II §D).</summary>
    [HttpGet("{id:int}/image")]
    public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
    {
        var image = await _service.GetImageAsync(id, cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        // A news image is immutable for a given version, and the news list re-renders
        // constantly - so without a validator every render re-downloaded every
        // picture in full, which on a metered phone connection is bytes the patient
        // pays for repeatedly. The ETag is the stored content hash, so serving it
        // costs nothing; ASP.NET Core turns a matching If-None-Match into a 304 and
        // skips the body entirely.
        return this.CacheableFile(image.Value.Data, image.Value.ContentType, image.Value.ContentHash, fileName: null);
    }
}
