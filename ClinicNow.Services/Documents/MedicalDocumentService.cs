using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Documents;

public class MedicalDocumentService : IMedicalDocumentService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MedicalDocumentService(ClinicNowContext context, IMapper mapper, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagedResult<MedicalDocumentDto>> GetPagedAsync(MedicalDocumentSearchObject search, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        IQueryable<MedicalDocument> query = _context.MedicalDocuments
            .Include(d => d.Patient)
            .Include(d => d.UploadedByUser);

        if (principal.IsInRole(Roles.Patient))
        {
            // A patient can never browse another patient's documents, regardless
            // of what patientId the client sends - ownership is always resolved
            // from the JWT, never trusted from the query string.
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            query = query.Where(d => d.PatientId == ownPatientId);
        }
        else if (search.PatientId.HasValue)
        {
            query = query.Where(d => d.PatientId == search.PatientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.FileName))
        {
            query = query.Where(d => d.FileName.Contains(search.FileName));
        }

        query = query.OrderByDescending(d => d.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);
        var entities = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<MedicalDocumentDto>
        {
            Count = count,
            ResultList = _mapper.Map<List<MedicalDocumentDto>>(entities)
        };
    }

    public async Task<MedicalDocumentDto> UploadAsync(MedicalDocumentInsertRequest request, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        var actingUserId = CurrentUserId(principal);

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ValidationException("fileName", "Naziv fajla je obavezan.");
        }

        var patientExists = await _context.Patients.AnyAsync(p => p.Id == request.PatientId, cancellationToken);
        if (!patientExists)
        {
            throw new ValidationException("patientId", "Odabrani pacijent ne postoji.");
        }

        var bytes = FileValidation.DecodeAndValidateFile(request.FileBase64, request.ContentType);

        var document = new MedicalDocument
        {
            PatientId = request.PatientId,
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType,
            FileData = bytes,
            FileSizeBytes = bytes.LongLength,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            UploadedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.MedicalDocuments.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.MedicalDocuments
            .Include(d => d.Patient)
            .Include(d => d.UploadedByUser)
            .SingleAsync(d => d.Id == document.Id, cancellationToken);

        return _mapper.Map<MedicalDocumentDto>(reloaded);
    }

    public async Task<MedicalDocument> GetFileForDownloadAsync(int id, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        var document = await _context.MedicalDocuments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalDocument), id);

        if (principal.IsInRole(Roles.Patient))
        {
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            if (document.PatientId != ownPatientId)
            {
                throw new ForbiddenException("Ne možete preuzeti tuđi dokument.");
            }
        }

        return document;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await _context.MedicalDocuments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalDocument), id);

        document.IsDeleted = true;
        document.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GetOwnPatientIdAsync(int userId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");
        return patient.Id;
    }

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
