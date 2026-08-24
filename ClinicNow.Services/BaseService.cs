using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
// ClinicNow.Model.Common is deliberately not `using`-imported here: it defines its
// own PagedResult<T>, which collides with System.Linq.Dynamic.Core.PagedResult<T>
// (used internally by the OrderBy(string) extension below). Referenced fully
// qualified instead so both packages' types stay unambiguous.

namespace ClinicNow.Services;

/// <summary>
/// Generic read-only service: paging, an entity-specific filter hook, and dynamic
/// sorting - shared by every entity in the system. Concrete services extend this (or
/// <see cref="BaseCRUDService{TModel,TSearch,TDbEntity,TInsert,TUpdate}"/> for
/// writes) and override <see cref="ApplyFilter"/> for their own search behaviour.
///
/// Registered as <c>Scoped</c> in DI (it holds a <see cref="ClinicNowContext"/>) -
/// never <c>Transient</c>, per rulebook Part II §D.
/// </summary>
public abstract class BaseService<TModel, TSearch, TDbEntity> : IService<TModel, TSearch>
    where TSearch : BaseSearchObject
    where TDbEntity : class
    where TModel : class
{
    protected readonly ClinicNowContext Context;
    protected readonly IMapper Mapper;

    protected BaseService(ClinicNowContext context, IMapper mapper)
    {
        Context = context;
        Mapper = mapper;
    }

    public virtual async Task<ClinicNow.Model.Common.PagedResult<TModel>> GetPagedAsync(TSearch search, CancellationToken cancellationToken = default)
    {
        var query = Context.Set<TDbEntity>().AsQueryable();
        query = ApplyFilter(search, query);

        // Count before paging (and after filtering) so clients can render page controls.
        var count = await query.CountAsync(cancellationToken);

        query = ApplySorting(search, query);

        query = query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize); // PageSize is already capped by BaseSearchObject.

        var entities = await query.ToListAsync(cancellationToken);

        return new ClinicNow.Model.Common.PagedResult<TModel>
        {
            Count = count,
            ResultList = Mapper.Map<List<TModel>>(entities)
        };
    }

    public virtual async Task<TModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Set<TDbEntity>().FindAsync([id], cancellationToken);
        return entity is null ? null : Mapper.Map<TModel>(entity);
    }

    /// <summary>
    /// Entity-specific WHERE clauses, applied against the database (never against an
    /// in-memory list - rulebook Part II §D: "Filtriranje... treba raditi na nivou
    /// baze"). Base implementation is a no-op; override per entity.
    /// </summary>
    protected virtual IQueryable<TDbEntity> ApplyFilter(TSearch search, IQueryable<TDbEntity> query) => query;

    /// <summary>
    /// Applies <see cref="BaseSearchObject.OrderBy"/>/<see cref="BaseSearchObject.SortDirection"/>
    /// via System.Linq.Dynamic.Core. An unknown/invalid column name is ignored rather
    /// than turning into a 500 for the whole list.
    /// </summary>
    private static IQueryable<TDbEntity> ApplySorting(TSearch search, IQueryable<TDbEntity> query)
    {
        if (string.IsNullOrWhiteSpace(search.OrderBy))
        {
            return query;
        }

        var direction = (search.SortDirection ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "desc" or "descending" => "descending",
            _ => "ascending"
        };

        try
        {
            return query.OrderBy($"{search.OrderBy} {direction}");
        }
        catch (Exception)
        {
            return query;
        }
    }
}
