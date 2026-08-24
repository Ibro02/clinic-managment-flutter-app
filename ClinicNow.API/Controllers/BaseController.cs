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

    [HttpGet]
    public virtual async Task<ActionResult<Model.Common.PagedResult<TModel>>> GetPaged(
        [FromQuery] TSearch search, CancellationToken cancellationToken)
    {
        return Ok(await Service.GetPagedAsync(search, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public virtual async Task<ActionResult<TModel>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await Service.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
