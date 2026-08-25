using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using ClinicNow.Services.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.API.Controllers;

/// <summary>
/// News/announcements. Reads open to any authenticated role (desktop staff and
/// mobile patients both browse this); writes Administrator/Staff only.
/// </summary>
public class NewsItemController : BaseCRUDController<NewsItemDto, NewsItemSearchObject, NewsItemInsertRequest, NewsItemUpdateRequest>
{
    private readonly ClinicNowContext _context;

    public NewsItemController(
        ICRUDService<NewsItemDto, NewsItemSearchObject, NewsItemInsertRequest, NewsItemUpdateRequest> service,
        ClinicNowContext context)
        : base(service)
    {
        _context = context;
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
        var entity = await _context.NewsItems.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new NotFoundException("NewsItem", id);

        if (entity.ImageData is null || entity.ImageData.Length == 0)
        {
            return NotFound();
        }

        return File(entity.ImageData, entity.ImageContentType ?? "application/octet-stream");
    }
}
