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

        // No Include here on purpose: the projection below pulls exactly the columns
        // the DTO needs, and EF generates the joins from it. Including the
        // navigations instead would materialize whole entities - and a whole
        // MedicalDocument entity carries FileData, a blob of up to 10 MB per row
        // that this endpoint never returns (rulebook Part II §D: list endpoints
        // return display data only, never file blobs).
        IQueryable<MedicalDocument> query = _context.MedicalDocuments;

        if (principal.IsInRole(Roles.Patient))
        {
            // A patient can never browse another patient's documents, regardless
            // of what patientId the client sends - ownership is always resolved
            // from the JWT, never trusted from the query string.
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            query = query.Where(d => d.PatientId == ownPatientId);
        }
        else
        {
            if (IsDoctorWithoutClinicWideAccess(principal))
            {
                // Same minimum-necessary rule the download enforces, applied as a
                // filter so the list can never advertise a document the doctor
                // would be refused on click. Expressed as a subquery rather than a
                // pre-fetched id list so it stays one round-trip and one plan.
                var doctorId = await GetOwnDoctorIdAsync(CurrentUserId(principal), cancellationToken);
                query = query.Where(d => _context.Appointments.Any(a =>
                    a.DoctorId == doctorId
                    && a.PatientId == d.PatientId
                    && a.Status != Model.Common.AppointmentStatus.Cancelled));
            }

            if (search.PatientId.HasValue)
            {
                query = query.Where(d => d.PatientId == search.PatientId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(search.FileName))
        {
            query = query.Where(d => d.FileName.Contains(search.FileName));
        }

        query = query.OrderByDescending(d => d.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);

        // Projected to the DTO in the query rather than materializing entities and
        // mapping afterwards. The mapping is spelled out here instead of reusing
        // MedicalDocumentMappingConfig because that config builds DownloadUrl with
        // string interpolation, which lowers to string.Format and has no SQL
        // translation - so ProjectToType would fail at runtime. The null-guard on
        // Patient mirrors that config deliberately (an archived patient still has
        // readable documents, review item C3).
        var rows = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(d => new MedicalDocumentDto
            {
                Id = d.Id,
                PatientId = d.PatientId,
                PatientName = d.Patient == null
                    ? "Obrisani pacijent"
                    : d.Patient.FirstName + " " + d.Patient.LastName,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                Description = d.Description,
                UploadedByName = d.UploadedByUser.FirstName + " " + d.UploadedByUser.LastName,
                CreatedAtUtc = d.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // Pure function of Id, so it costs nothing to fill in here and keeps the
        // projection above translatable.
        foreach (var row in rows)
        {
            row.DownloadUrl = $"/api/MedicalDocument/{row.Id}/download";
        }

        return new PagedResult<MedicalDocumentDto>
        {
            Count = count,
            ResultList = rows
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
            ContentHash = Documents.ContentHash.Compute(bytes),
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
        else if (IsDoctorWithoutClinicWideAccess(principal))
        {
            await EnsureDoctorHasTreatedAsync(principal, document.PatientId, cancellationToken);
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

    /// <summary>
    /// True for a caller acting purely as a doctor. Administrator and Staff run the
    /// clinic's records and legitimately see every patient, so a user holding either
    /// of those roles alongside Doctor is not narrowed.
    /// </summary>
    private static bool IsDoctorWithoutClinicWideAccess(ClaimsPrincipal principal) =>
        principal.IsInRole(Roles.Doctor)
        && !principal.IsInRole(Roles.Administrator)
        && !principal.IsInRole(Roles.Staff);

    /// <summary>
    /// Minimum-necessary access: a doctor reads a patient's file only if they have
    /// actually treated that patient. Being a doctor previously granted read access
    /// to every patient's documents in the clinic with no treating relationship of
    /// any kind - for medical records that is exactly the access-control gap the
    /// principle exists to prevent.
    ///
    /// A cancelled appointment does not count: it means the visit never happened.
    /// </summary>
    private async Task EnsureDoctorHasTreatedAsync(ClaimsPrincipal principal, int patientId, CancellationToken cancellationToken)
    {
        var doctorId = await GetOwnDoctorIdAsync(CurrentUserId(principal), cancellationToken);

        var hasTreated = await _context.Appointments.AnyAsync(a =>
            a.DoctorId == doctorId
            && a.PatientId == patientId
            && a.Status != Model.Common.AppointmentStatus.Cancelled, cancellationToken);

        if (!hasTreated)
        {
            throw new ForbiddenException(
                "Nemate pristup dokumentaciji pacijenta kojeg niste liječili.");
        }
    }

    private async Task<int> GetOwnDoctorIdAsync(int userId, CancellationToken cancellationToken)
    {
        var doctor = await _context.Doctors.SingleOrDefaultAsync(d => d.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen doktorski profil za ovaj nalog.");
        return doctor.Id;
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
