using ClinicNow.Model.SearchObjects;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Generic read-only controller: paged listing + get-by-id. Contains no business
/// logic and never touches the DbContext directly - it only forwards to
/// <see cref="IService{TModel,TSearch}"/> (rulebook Part II §D: Controller -&gt;
/// Service -&gt; DbContext).
///
/// <c>[Authorize]</c> by default (rulebook §5: "[Authorize] mora biti postavljen na
/// svim kontrolerima koji pristupaju korisnickim podacima"). The rare
/// publicly-readable controller overrides this explicitly with <c>[AllowAnonymous]</c>
/// on the specific action, not by removing this class-level attribute.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseController<TModel, TSearch> : ControllerBase
    where TModel : class
    where TSearch : BaseSearchObject
{
    protected readonly IService<TModel, TSearch> Service;

    protected BaseController(IService<TModel, TSearch> service)
    {
        Service = service;
    }

    /// <summary>
    /// The parameter is named <c>criteria</c>, not <c>search</c>, and that name
    /// is load-bearing. ASP.NET Core binds a complex <c>[FromQuery]</c> object
    /// with an empty prefix only while no query key matches the parameter's own
    /// name; the moment one does, it switches to prefixed binding and expects
    /// <c>search.Foo=…</c>. A search object with a plain <c>Search</c> property
    /// (LabFinding, Referral, Diagnosis) would therefore have its filter
    /// silently dropped for <c>?search=…</c> - the request still returns 200,
    /// just unfiltered. Renaming the parameter removes the collision for every
    /// search object at once. Don't rename it back.
    /// </summary>
    [HttpGet]
    public virtual async Task<ActionResult<Model.Common.PagedResult<TModel>>> GetPaged(
        [FromQuery] TSearch criteria, CancellationToken cancellationToken)
    {
        return Ok(await Service.GetPagedAsync(criteria, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public virtual async Task<ActionResult<TModel>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await Service.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
