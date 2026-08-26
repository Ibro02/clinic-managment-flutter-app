using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Recommender;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Explainable appointment recommendations (recommender-dokumentacija.md).
/// Patient-only: this is a self-service, per-patient feature - staff/doctor/
/// admin have no equivalent use for it in this seminar's scope. `UserId` is
/// always resolved from the JWT inside <see cref="IRecommenderService"/>,
/// never from the route or body (rulebook §5).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Patient)]
public class RecommendationController : ControllerBase
{
    private readonly IRecommenderService _service;

    public RecommendationController(IRecommenderService service)
    {
        _service = service;
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<List<AppointmentRecommendationDto>>> GetAppointmentRecommendations(CancellationToken cancellationToken) =>
        Ok(await _service.GetRecommendationsAsync(cancellationToken));

    [HttpPost("interaction")]
    public async Task<IActionResult> LogInteraction(RecommenderInteractionRequest request, CancellationToken cancellationToken)
    {
        await _service.LogInteractionAsync(request, cancellationToken);
        return NoContent();
    }
}
