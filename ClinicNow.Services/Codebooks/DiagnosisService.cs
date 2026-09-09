using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Codebooks;

/// <summary>
/// CRUD over the diagnosis codebook. Same shape as every other codebook service
/// (rulebook §2.2: every reference table gets management forms), with the one
/// addition that a diagnosis already referenced by a medical-record entry cannot
/// be deleted - rulebook §3.1 requires the delete to be blocked with a reason the
/// user can act on, rather than surfacing an FK violation.
/// </summary>
public class DiagnosisService
    : BaseCRUDService<DiagnosisDto, DiagnosisSearchObject, Diagnosis, DiagnosisInsertRequest, DiagnosisUpdateRequest>
{
    public DiagnosisService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<Diagnosis> ApplyFilter(DiagnosisSearchObject search, IQueryable<Diagnosis> query)
    {
        // Included so the DTO can carry the specialization's *name*; a raw id is
        // never shown in the UI (rulebook §6).
        query = query.Include(d => d.SuggestedSpecialization);

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(d => d.Code.Contains(term) || d.Name.Contains(term));
        }

        if (search.SuggestedSpecializationId.HasValue)
        {
            query = query.Where(d => d.SuggestedSpecializationId == search.SuggestedSpecializationId.Value);
        }

        return query;
    }

    protected override async Task BeforeInsertAsync(DiagnosisInsertRequest request, Diagnosis entity, CancellationToken cancellationToken)
    {
        entity.Code = NormalizeAndValidateCode(request.Code);
        entity.Name = NormalizeAndValidateName(request.Name);
        await EnsureCodeIsUniqueAsync(entity.Code, excludeId: null, cancellationToken);
        await EnsureSpecializationExistsAsync(request.SuggestedSpecializationId, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(DiagnosisUpdateRequest request, Diagnosis entity, CancellationToken cancellationToken)
    {
        var code = NormalizeAndValidateCode(request.Code);
        await EnsureCodeIsUniqueAsync(code, excludeId: entity.Id, cancellationToken);
        await EnsureSpecializationExistsAsync(request.SuggestedSpecializationId, cancellationToken);

        request.Code = code;
        request.Name = NormalizeAndValidateName(request.Name);
    }

    protected override async Task BeforeDeleteAsync(Diagnosis entity, CancellationToken cancellationToken)
    {
        var usageCount = await Context.MedicalRecordEntries
            .IgnoreQueryFilters()
            .CountAsync(e => e.DiagnosisId == entity.Id, cancellationToken);

        if (usageCount > 0)
        {
            throw new BusinessException(
                $"Dijagnoza '{entity.Code} - {entity.Name}' se ne može obrisati jer je iskorištena u {usageCount} zapisa medicinskog kartona.");
        }
    }

    private static string NormalizeAndValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException("code", "Šifra dijagnoze (MKB-10) je obavezna, npr. J06.9.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > 10)
        {
            throw new ValidationException("code", "Šifra dijagnoze može imati najviše 10 znakova, npr. J06.9.");
        }

        return normalized;
    }

    private static string NormalizeAndValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "Naziv dijagnoze je obavezan.");
        }

        return name.Trim();
    }

    private async Task EnsureCodeIsUniqueAsync(string code, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await Context.Diagnoses.AnyAsync(
            d => d.Code == code && d.Id != (excludeId ?? 0), cancellationToken);

        if (exists)
        {
            throw new ValidationException("code", $"Dijagnoza sa šifrom '{code}' već postoji.");
        }
    }

    private async Task EnsureSpecializationExistsAsync(int? specializationId, CancellationToken cancellationToken)
    {
        if (specializationId is null)
        {
            return;
        }

        var exists = await Context.Specializations.AnyAsync(s => s.Id == specializationId.Value, cancellationToken);
        if (!exists)
        {
            throw new ValidationException("suggestedSpecializationId", "Odabrana specijalizacija ne postoji.");
        }
    }
}
