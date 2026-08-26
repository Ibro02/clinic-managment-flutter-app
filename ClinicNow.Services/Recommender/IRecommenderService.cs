using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Recommender;

/// <summary>
/// Content-based (+ popularity fallback) appointment recommender
/// (recommender-dokumentacija.md). Bespoke, like <c>IAppointmentService</c> -
/// this isn't CRUD, and ownership (the caller's own Patient row) needs an
/// async JWT-driven lookup the generic <c>ICRUDService</c> shape doesn't fit.
/// </summary>
public interface IRecommenderService
{
    /// <summary>Top-N ranked, explainable suggestions for the calling patient (resolved from the JWT - never a route/body id).</summary>
    Task<List<AppointmentRecommendationDto>> GetRecommendationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Records one real view/search interaction for the calling patient (doc §3) - this is the only way a <c>RecommenderInteraction</c> row is ever created.</summary>
    Task LogInteractionAsync(RecommenderInteractionRequest request, CancellationToken cancellationToken = default);
}
