using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// The current user's own in-app notifications - no generic CRUD base class fits
/// here (there's no client-facing Insert, and the whole read surface is implicitly
/// scoped to the caller, not a generic paged list of "all" notifications).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationController(INotificationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetPaged(
        [FromQuery] NotificationSearchObject criteria, CancellationToken cancellationToken) =>
        Ok(await _service.GetPagedAsync(criteria, cancellationToken));

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount(CancellationToken cancellationToken) =>
        Ok(await _service.GetUnreadCountAsync(cancellationToken));

    [HttpPost("{id:int}/mark-read")]
    public async Task<IActionResult> MarkAsRead(int id, CancellationToken cancellationToken)
    {
        await _service.MarkAsReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        await _service.MarkAllAsReadAsync(cancellationToken);
        return NoContent();
    }
}
