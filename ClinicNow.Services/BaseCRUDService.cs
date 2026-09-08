using ClinicNow.Model.Exceptions;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services;

/// <summary>
/// Adds Insert/Update/Delete on top of <see cref="BaseService{TModel,TSearch,TDbEntity}"/>.
/// Override the <c>Before*</c>/<c>After*</c> hooks for entity-specific validation and
/// side effects - controllers never contain this logic themselves (rulebook Part II
/// §D: Controller -&gt; Service -&gt; DbContext, no business logic in the controller).
///
/// A single <c>SaveChangesAsync</c> call per Insert/Update/Delete is already atomic.
/// If an overridden <c>After*Async</c> hook needs a <em>second</em> SaveChanges (e.g.
/// inserting related child rows), wrap that specific operation in an explicit
/// <c>IDbContextTransaction</c> - see rulebook Part II §D.
/// </summary>
public abstract class BaseCRUDService<TModel, TSearch, TDbEntity, TInsert, TUpdate>
    : BaseService<TModel, TSearch, TDbEntity>, ICRUDService<TModel, TSearch, TInsert, TUpdate>
    where TSearch : BaseSearchObject
    where TDbEntity : class
    where TModel : class
{
    protected BaseCRUDService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    public virtual async Task<TModel> InsertAsync(TInsert request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = Mapper.Map<TDbEntity>(request);

        await BeforeInsertAsync(request, entity, cancellationToken);

        Context.Set<TDbEntity>().Add(entity);
        await Context.SaveChangesAsync(cancellationToken);

        await AfterInsertAsync(request, entity, cancellationToken);

        return Mapper.Map<TModel>(entity);
    }

    /// <summary>Validate the request / throw <see cref="BusinessException"/> before the row is added.</summary>
    protected virtual Task BeforeInsertAsync(TInsert request, TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs after the initial insert is saved (e.g. persist related rows, enqueue a message).</summary>
    protected virtual Task AfterInsertAsync(TInsert request, TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async Task<TModel> UpdateAsync(int id, TUpdate request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = await Context.Set<TDbEntity>().FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(typeof(TDbEntity).Name, id);

        await BeforeUpdateAsync(request, entity, cancellationToken);

        Mapper.Map(request, entity);

        await Context.SaveChangesAsync(cancellationToken);

        await AfterUpdateAsync(request, entity, cancellationToken);

        return Mapper.Map<TModel>(entity);
    }

    protected virtual Task BeforeUpdateAsync(TUpdate request, TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task AfterUpdateAsync(TUpdate request, TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Set<TDbEntity>().FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(typeof(TDbEntity).Name, id);

        // Entity-specific "is this row referenced elsewhere" guards belong here -
        // throw a BusinessException with a clear, user-facing reason (rulebook
        // Part II §A: deletion must be blocked with a clear message when referenced).
        await BeforeDeleteAsync(entity, cancellationToken);

        if (entity is ISoftDelete softDelete)
        {
            softDelete.IsDeleted = true;
            softDelete.DeletedAtUtc = DateTime.UtcNow;
            Context.Update(entity);
        }
        else
        {
            Context.Set<TDbEntity>().Remove(entity);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Safety net for FK-constraint violations any concrete service forgot to
            // guard explicitly in BeforeDeleteAsync - never leak the raw SQL error.
            throw new BusinessException(
                "Zapis se ne može obrisati jer je povezan sa drugim podacima u sistemu.", ex);
        }

        await AfterDeleteAsync(entity, cancellationToken);
    }

    /// <summary>Guard hook: throw <see cref="BusinessException"/> if the entity is still referenced elsewhere.</summary>
    protected virtual Task BeforeDeleteAsync(TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Runs after the delete/archive has been committed - e.g. evicting a cache
    /// entry that must reflect what was just persisted, not what SaveChangesAsync
    /// is about to overwrite.
    /// </summary>
    protected virtual Task AfterDeleteAsync(TDbEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;
}
