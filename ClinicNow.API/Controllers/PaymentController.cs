using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Payments (Phase 8, design doc). Create/capture are Patient-only (a
/// patient paying for their own appointment); refund is Administrator/Staff
/// only (the manual half of design doc §2's refund trigger - the automatic
/// half runs inside <c>AppointmentService.CancelAsync</c>, not through this
/// controller at all).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _service;

    public PaymentController(IPaymentService service)
    {
        _service = service;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("{id:int}/capture")]
    [Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<PaymentDto>> Capture(int id, CancellationToken cancellationToken) =>
        Ok(await _service.CaptureAsync(id, cancellationToken));

    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public async Task<ActionResult<PaymentDto>> Refund(int id, PaymentRefundRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.RefundAsync(id, request, cancellationToken));

    [HttpGet("by-appointment/{appointmentId:int}")]
    public async Task<ActionResult<PaymentDto>> GetByAppointment(int appointmentId, CancellationToken cancellationToken)
    {
        var payment = await _service.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        return payment is null ? NotFound() : Ok(payment);
    }
}
