using ClinicNow.Model.Requests;
using ClinicNow.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Push-notification device registration for the signed-in user.
///
/// Write-only on purpose: there is no GET. A device token is an opaque routing
/// detail with no feature that needs to read it back, and listing a person's
/// devices would be a privacy surface serving nothing.
///
/// Every action takes the owner from the JWT, so no route or body carries a user
/// id (rulebook Part II §F).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceTokenController : ControllerBase
{
    private readonly IDeviceTokenService _service;

    public DeviceTokenController(IDeviceTokenService service)
    {
        _service = service;
    }

    /// <summary>Registers or refreshes the calling device. Idempotent.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        await _service.RegisterAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Removes the calling device, on sign-out.</summary>
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister(UnregisterDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        await _service.UnregisterAsync(request, cancellationToken);
        return NoContent();
    }
}
