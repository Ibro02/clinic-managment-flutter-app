using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Localization;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Notifications;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Documents;

public class LabFindingService : ILabFindingService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notificationService;

    public LabFindingService(
        ClinicNowContext context,
        IMapper mapper,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notificationService)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public async Task<PagedResult<LabFindingDto>> GetPagedAsync(LabFindingSearchObject search, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        // No Include: the projection at the end of this method selects exactly the
        // DTO's columns and EF derives the joins from it. Materializing entities
        // instead would read FileData - up to 10 MB of PDF per row that this
        // endpoint never returns (rulebook Part II §D).
        IQueryable<LabFinding> query = _context.LabFindings;

        if (principal.IsInRole(Roles.Patient))
        {
            // A patient can never browse another patient's findings, regardless
            // of what patientId the client sends - ownership is always resolved
            // from the JWT, never trusted from the query string.
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            query = query.Where(f => f.PatientId == ownPatientId);
        }
        else
        {
            if (search.PatientId.HasValue)
            {
                query = query.Where(f => f.PatientId == search.PatientId.Value);
            }

            if (search.AppointmentId.HasValue)
            {
                query = query.Where(f => f.AppointmentId == search.AppointmentId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(f =>
                f.TestName.Contains(term)
                || f.Result.Contains(term)
                || (f.FileName != null && f.FileName.Contains(term)));
        }

        query = query.OrderByDescending(f => f.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);

        // Spelled out rather than reusing LabFindingMappingConfig for the same
        // reason as MedicalDocumentService: that config builds DownloadUrl by
        // string interpolation, which has no SQL translation. The Patient null-guard
        // mirrors it deliberately (archived patient, review item C3).
        var rows = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(f => new LabFindingDto
            {
                Id = f.Id,
                PatientId = f.PatientId,
                PatientName = f.Patient == null
                    ? "Obrisani pacijent"
                    : f.Patient.FirstName + " " + f.Patient.LastName,
                AppointmentId = f.AppointmentId,
                AppointmentStartUtc = f.Appointment.StartUtc,
                MedicalServiceName = f.Appointment.MedicalService.Name,
                TestName = f.TestName,
                Value = f.Value,
                Unit = f.Unit,
                ReferenceRange = f.ReferenceRange,
                Result = f.Result,
                DoctorNote = f.DoctorNote,
                FileName = f.FileName,
                ContentType = f.ContentType,
                FileSizeBytes = f.FileSizeBytes,
                // Translated to SQL as a length test on the blob column, so the
                // bytes themselves are never read into memory - the whole reason
                // this projection exists instead of materializing entities.
                HasFile = f.FileData != null && f.FileData.Length > 0,
                EnteredByName = f.EnteredByUser.FirstName + " " + f.EnteredByUser.LastName,
                CreatedAtUtc = f.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            // Left empty when there is nothing attached, so a client can never
            // offer a download that would 404.
            row.DownloadUrl = row.HasFile ? $"/api/LabFinding/{row.Id}/download" : string.Empty;
        }

        return new PagedResult<LabFindingDto>
        {
            Count = count,
            ResultList = rows
        };
    }

    public async Task<LabFindingDto> CreateAsync(LabFindingInsertRequest request, CancellationToken cancellationToken = default)
    {
        var actingUserId = CurrentUserId(CurrentUser());

        if (string.IsNullOrWhiteSpace(request.TestName))
        {
            throw new ValidationException("testName", "Naziv pretrage je obavezan.");
        }

        if (string.IsNullOrWhiteSpace(request.Result))
        {
            throw new ValidationException("result", "Nalaz je obavezan.");
        }

        // A unit or a reference range without a measured value describes nothing -
        // catch that here rather than storing a row the UI cannot render sensibly.
        if (string.IsNullOrWhiteSpace(request.Value)
            && (!string.IsNullOrWhiteSpace(request.Unit) || !string.IsNullOrWhiteSpace(request.ReferenceRange)))
        {
            throw new ValidationException("value", "Unesite izmjerenu vrijednost ako navodite jedinicu mjere ili referentni opseg.");
        }

        // The appointment is the single source of truth for which patient this
        // finding belongs to - never trust a separate client-supplied patientId
        // that could disagree with it (review item C4: "veza prema pacijentu i
        // terminu" must actually be consistent, not two independently-trusted
        // client inputs).
        var appointment = await _context.Appointments.SingleOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new ValidationException("appointmentId", "Odabrani termin ne postoji.");

        // The attachment is optional (prijava: "uz nalaz se *može* priložiti i
        // dokument"). Supplying one still enforces the full MIME + magic-byte
        // check; supplying only half of it is a validation error rather than a
        // silently half-stored file.
        byte[]? bytes = null;
        var hasFile = !string.IsNullOrWhiteSpace(request.FileBase64);
        if (hasFile)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                throw new ValidationException("fileName", "Naziv fajla je obavezan kada prilažete dokument.");
            }

            bytes = FileValidation.DecodeAndValidateFile(request.FileBase64!, request.ContentType ?? string.Empty);
        }
        else if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ValidationException("fileBase64", "Odaberite dokument ili uklonite naziv fajla.");
        }

        var finding = new LabFinding
        {
            PatientId = appointment.PatientId,
            AppointmentId = appointment.Id,
            TestName = request.TestName.Trim(),
            Value = Normalize(request.Value),
            Unit = Normalize(request.Unit),
            ReferenceRange = Normalize(request.ReferenceRange),
            Result = request.Result.Trim(),
            DoctorNote = Normalize(request.DoctorNote),
            FileName = bytes is null ? null : request.FileName!.Trim(),
            ContentType = bytes is null ? null : request.ContentType,
            FileData = bytes,
            ContentHash = bytes is null ? null : Documents.ContentHash.Compute(bytes),
            FileSizeBytes = bytes?.LongLength ?? 0,
            EnteredByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.LabFindings.Add(finding);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.LabFindings
            .Include(f => f.Patient).ThenInclude(p => p!.User)
            .Include(f => f.Appointment).ThenInclude(a => a.MedicalService)
            .Include(f => f.EnteredByUser)
            .SingleAsync(f => f.Id == finding.Id, cancellationToken);

        // The prijava promises a notification for "novi laboratorijski nalaz",
        // and rulebook §7.2 requires notifications for every relevant event -
        // not only the booking ones. Guarded on User because Patient.UserId is
        // nullable: a patient created by staff need not have a login yet, and
        // there is then nobody to notify.
        if (reloaded.Patient?.User is not null)
        {
            var message = PatientMessages.LabFindingAdded(
                reloaded.Patient.User.PreferredLanguage,
                reloaded.Appointment.MedicalService.Name,
                reloaded.Appointment.StartUtc);

            await _notificationService.CreateAsync(
                reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);
        }

        return _mapper.Map<LabFindingDto>(reloaded);
    }

    public async Task<LabFinding> GetFileForDownloadAsync(int id, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        var finding = await _context.LabFindings.SingleOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LabFinding), id);

        // Checked before the ownership rules on purpose: "there is no document"
        // is not an authorization answer, and answering it first keeps the
        // 403 reserved for genuinely forbidden access.
        if (!finding.HasFile)
        {
            throw new BusinessException("Uz ovaj nalaz nije priložen dokument.");
        }

        if (principal.IsInRole(Roles.Patient))
        {
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            if (finding.PatientId != ownPatientId)
            {
                throw new ForbiddenException("Ne možete preuzeti tuđi nalaz.");
            }
        }
        else if (principal.IsInRole(Roles.Doctor)
            && !principal.IsInRole(Roles.Administrator)
            && !principal.IsInRole(Roles.Staff))
        {
            // Minimum-necessary access, same rule MedicalDocumentService enforces: a
            // lab finding is as sensitive as any other record, so being a doctor is
            // not on its own a reason to read one for a patient never treated.
            var doctorId = await _context.Doctors
                .Where(d => d.UserId == CurrentUserId(principal))
                .Select(d => (int?)d.Id)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Nije pronađen doktorski profil za ovaj nalog.");

            var hasTreated = await _context.Appointments.AnyAsync(a =>
                a.DoctorId == doctorId
                && a.PatientId == finding.PatientId
                && a.Status != AppointmentStatus.Cancelled, cancellationToken);

            if (!hasTreated)
            {
                throw new ForbiddenException("Nemate pristup nalazima pacijenta kojeg niste liječili.");
            }
        }

        return finding;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var finding = await _context.LabFindings.SingleOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LabFinding), id);

        finding.IsDeleted = true;
        finding.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Trims an optional field, collapsing whitespace-only input to null so "empty" has one representation in the database.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
