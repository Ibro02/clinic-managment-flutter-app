using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services;

/// <summary>Adds write operations on top of <see cref="IService{TModel,TSearch}"/>.</summary>
public interface ICRUDService<TModel, in TSearch, in TInsert, in TUpdate> : IService<TModel, TSearch>
    where TSearch : BaseSearchObject
{
    Task<TModel> InsertAsync(TInsert request, CancellationToken cancellationToken = default);
    Task<TModel> UpdateAsync(int id, TUpdate request, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes if the entity implements <see cref="Database.ISoftDelete"/>, else hard-deletes.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
