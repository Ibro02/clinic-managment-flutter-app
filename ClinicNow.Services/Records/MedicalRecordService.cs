using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Records;

public class MedicalRecordService : IMedicalRecordService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MedicalRecordService(ClinicNowContext context, IMapper mapper, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<MedicalRecordDto> GetByPatientIdAsync(int patientId, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        await EnsureCanViewAsync(principal, patientId, cancellationToken);

        var record = await LoadFullRecordAsync(patientId, cancellationToken);
        return _mapper.Map<MedicalRecordDto>(record);
    }

    public async Task<MedicalRecordDto> AppendNotesAsync(int patientId, MedicalRecordAppendNotesRequest request, CancellationToken cancellationToken = default)
    {
        // Doctor-only action (rulebook: "doctors can add content but not delete
        // it after it's applied") - enforced here rather than trusted from the
        // controller's [Authorize], since this method's own semantics (append,
        // never replace) are what actually protects prior content.
        var record = await GetTrackedAsync(patientId, cancellationToken);
        var actingUserId = CurrentUserId(CurrentUser());

        if (!string.IsNullOrWhiteSpace(request.AllergiesToAppend))
        {
            record.Allergies = AppendText(record.Allergies, request.AllergiesToAppend);
        }

        if (!string.IsNullOrWhiteSpace(request.MedicalNotesToAppend))
        {
            record.MedicalNotes = AppendText(record.MedicalNotes, request.MedicalNotesToAppend);
        }

        record.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditLog(record, MedicalRecordAuditAction.NotesAppended, actingUserId, "Dopisane alergije/napomene.");
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> ReplaceNotesAsync(int patientId, MedicalRecordUpdateNotesRequest request, CancellationToken cancellationToken = default)
    {
        var record = await GetTrackedAsync(patientId, cancellationToken);
        var actingUserId = CurrentUserId(CurrentUser());

        record.Allergies = string.IsNullOrWhiteSpace(request.Allergies) ? null : request.Allergies.Trim();
        record.MedicalNotes = string.IsNullOrWhiteSpace(request.MedicalNotes) ? null : request.MedicalNotes.Trim();
        record.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditLog(record, MedicalRecordAuditAction.NotesReplaced, actingUserId, "Zamijenjene alergije/napomene.");

        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> AddEntryAsync(int patientId, MedicalRecordEntryInsertRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEntry(request.Diagnosis, request.Treatment, request.Description);

        var record = await GetTrackedAsync(patientId, cancellationToken);
        var actingUserId = CurrentUserId(CurrentUser());
        var treatment = request.Treatment.Trim();

        _context.MedicalRecordEntries.Add(new MedicalRecordEntry
        {
            MedicalRecordId = record.Id,
            EntryDate = request.EntryDate,
            Diagnosis = request.Diagnosis.Trim(),
            Treatment = treatment,
            Description = request.Description.Trim(),
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        });

        record.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditLog(record, MedicalRecordAuditAction.EntryAdded, actingUserId, $"Unos dodan: {treatment}.");
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> UpdateEntryAsync(int entryId, MedicalRecordEntryUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEntry(request.Diagnosis, request.Treatment, request.Description);

        var entry = await _context.MedicalRecordEntries
            .Include(e => e.MedicalRecord)
            .SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalRecordEntry), entryId);
        var actingUserId = CurrentUserId(CurrentUser());
        var treatment = request.Treatment.Trim();

        entry.EntryDate = request.EntryDate;
        entry.Diagnosis = request.Diagnosis.Trim();
        entry.Treatment = treatment;
        entry.Description = request.Description.Trim();

        entry.MedicalRecord.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditLog(entry.MedicalRecord, MedicalRecordAuditAction.EntryUpdated, actingUserId, $"Unos izmijenjen: {treatment}.");

        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(entry.MedicalRecord.PatientId, cancellationToken));
    }

    public async Task DeleteEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.MedicalRecordEntries
            .Include(e => e.MedicalRecord)
            .SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalRecordEntry), entryId);
        var actingUserId = CurrentUserId(CurrentUser());

        // Soft-delete, not a physical removal (review item C11: "DeleteEntryAsync()
        // fizicki uklanja zapis bez historije promjene") - MedicalRecordEntry
        // implements ISoftDelete, so the global query filter makes it stop
        // appearing in LoadFullRecordAsync's Include(r => r.Entries) on its own,
        // with the row (and its audit trail) still present in the database.
        entry.IsDeleted = true;
        entry.DeletedAtUtc = DateTime.UtcNow;

        entry.MedicalRecord.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditLog(entry.MedicalRecord, MedicalRecordAuditAction.EntryDeleted, actingUserId, $"Unos obrisan: {entry.Treatment}.");

        await _context.SaveChangesAsync(cancellationToken);
    }

    // --- helpers -------------------------------------------------------------

    private async Task<MedicalRecord> LoadFullRecordAsync(int patientId, CancellationToken cancellationToken)
    {
        return await _context.MedicalRecords
            .Include(r => r.Patient)
            .Include(r => r.Entries).ThenInclude(e => e.CreatedByUser)
            .SingleOrDefaultAsync(r => r.PatientId == patientId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovog pacijenta.");
    }

    private async Task<MedicalRecord> GetTrackedAsync(int patientId, CancellationToken cancellationToken)
    {
        return await _context.MedicalRecords.SingleOrDefaultAsync(r => r.PatientId == patientId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovog pacijenta.");
    }

    /// <summary>
    /// Appends one audit row via the navigation collection (not a raw FK) so a
    /// row that's part of the same unit of work as its parent's other changes
    /// commits together in one <c>SaveChangesAsync</c> - same reasoning as
    /// <c>BaseAppointmentState.AddAuditLog</c>.
    /// </summary>
    private static void AddAuditLog(MedicalRecord record, MedicalRecordAuditAction action, int actingUserId, string? description)
    {
        record.AuditLogs.Add(new MedicalRecordAuditLog
        {
            Action = action,
            ActingUserId = actingUserId,
            OccurredAtUtc = DateTime.UtcNow,
            Description = description
        });
    }

    private async Task EnsureCanViewAsync(ClaimsPrincipal principal, int patientId, CancellationToken cancellationToken)
    {
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff) || principal.IsInRole(Roles.Doctor))
        {
            return;
        }

        if (principal.IsInRole(Roles.Patient))
        {
            var userId = CurrentUserId(principal);
            var ownPatient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");

            if (ownPatient.Id != patientId)
            {
                throw new ForbiddenException("Nemate pristup tuđem medicinskom kartonu.");
            }
            return;
        }

        throw new ForbiddenException("Nemate pristup medicinskom kartonu.");
    }

    private static string AppendText(string? existing, string addition)
    {
        // Human-readable date, so clinic-local (review item C11) - the audit
        // trail's own OccurredAtUtc is what stays UTC, not this.
        var trimmedAddition = $"[{ClinicTimeZone.NowLocal:dd.MM.yyyy}] {addition.Trim()}";
        return string.IsNullOrWhiteSpace(existing) ? trimmedAddition : $"{existing}\n{trimmedAddition}";
    }

    private static void ValidateEntry(string diagnosis, string treatment, string description)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(diagnosis))
        {
            errors["diagnosis"] = ["Dijagnoza je obavezna."];
        }

        if (string.IsNullOrWhiteSpace(treatment))
        {
            errors["treatment"] = ["Tretman je obavezan."];
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors["description"] = ["Opis je obavezan."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
