using ClinicNow.Model.SearchObjects;
using ClinicNow.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Adds Insert/Update/Delete on top of <see cref="BaseController{TModel,TSearch}"/>.
/// Write operations inherit the class-level <c>[Authorize]</c> - per rulebook §5,
/// POST/PUT/DELETE must never be anonymous.
/// </summary>
public abstract class BaseCRUDController<TModel, TSearch, TInsert, TUpdate>
    : BaseController<TModel, TSearch>
    where TModel : class
    where TSearch : BaseSearchObject
{
    protected new readonly ICRUDService<TModel, TSearch, TInsert, TUpdate> Service;

    protected BaseCRUDController(ICRUDService<TModel, TSearch, TInsert, TUpdate> service) : base(service)
    {
        Service = service;
    }

    [HttpPost]
    public virtual async Task<ActionResult<TModel>> Insert(TInsert request, CancellationToken cancellationToken)
    {
        var created = await Service.InsertAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:int}")]
    public virtual async Task<ActionResult<TModel>> Update(int id, TUpdate request, CancellationToken cancellationToken)
    {
        return Ok(await Service.UpdateAsync(id, request, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public virtual async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await Service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
