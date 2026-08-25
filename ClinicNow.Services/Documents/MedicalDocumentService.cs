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
    // 10 MB - generous for a scanned finding/report while still bounding request size.
    private const int MaxFileBytes = 10 * 1024 * 1024;

    // Every MIME type this endpoint accepts, and the magic bytes a genuine file of
    // that type must start with (rulebook Part II §F: "MIME + magic bytes, ne samo
    // ekstenzija" - the declared Content-Type alone is never trusted).
    private static readonly Dictionary<string, byte[][]> AllowedSignatures = new()
    {
        ["application/pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
    };

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

        var bytes = DecodeAndValidateFile(request.FileBase64, request.ContentType);

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

    private static byte[] DecodeAndValidateFile(string base64, string declaredContentType)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ValidationException("fileBase64", "Fajl je obavezan.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ValidationException("fileBase64", "Fajl nije ispravno Base64 kodiran.");
        }

        if (bytes.Length == 0)
        {
            throw new ValidationException("fileBase64", "Fajl je prazan.");
        }

        if (bytes.Length > MaxFileBytes)
        {
            throw new ValidationException("fileBase64", "Fajl je prevelik (maksimalno 10 MB).");
        }

        if (!AllowedSignatures.TryGetValue(declaredContentType, out var signatures))
        {
            throw new ValidationException("contentType", "Dozvoljeni su samo PDF, PNG i JPEG fajlovi.");
        }

        // The declared Content-Type alone proves nothing - a renamed .exe with a
        // "application/pdf" header would sail through that check alone. Confirm
        // the file's actual leading bytes match a real file of that type.
        var matchesSignature = signatures.Any(signature =>
            bytes.Length >= signature.Length && bytes.Take(signature.Length).SequenceEqual(signature));

        if (!matchesSignature)
        {
            throw new ValidationException("fileBase64", "Sadržaj fajla ne odgovara prijavljenom tipu (MIME provjera nije prošla).");
        }

        return bytes;
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
