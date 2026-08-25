using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Codebooks;

public class SpecializationService
    : BaseCRUDService<SpecializationDto, SpecializationSearchObject, Specialization, SpecializationInsertRequest, SpecializationUpdateRequest>
{
    public SpecializationService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<Specialization> ApplyFilter(SpecializationSearchObject search, IQueryable<Specialization> query)
    {
        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(s => s.Name.Contains(search.Name));
        }

        return query;
    }

    protected override async Task BeforeInsertAsync(SpecializationInsertRequest request, Specialization entity, CancellationToken cancellationToken)
    {
        entity.Name = NormalizeAndValidateName(request.Name);
        await EnsureNameIsUniqueAsync(entity.Name, excludeId: null, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(SpecializationUpdateRequest request, Specialization entity, CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeAndValidateName(request.Name);
        await EnsureNameIsUniqueAsync(normalizedName, excludeId: entity.Id, cancellationToken);
        request.Name = normalizedName;
    }

    private static string NormalizeAndValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "Naziv specijalizacije je obavezan.");
        }

        return name.Trim();
    }

    private async Task EnsureNameIsUniqueAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await Context.Specializations.AnyAsync(
            s => s.Name == name && s.Id != (excludeId ?? 0), cancellationToken);

        if (exists)
        {
            throw new ValidationException("name", $"Specijalizacija sa nazivom '{name}' već postoji.");
        }
    }
}
