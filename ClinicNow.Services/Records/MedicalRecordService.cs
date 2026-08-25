using System.Security.Claims;
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

        if (!string.IsNullOrWhiteSpace(request.AllergiesToAppend))
        {
            record.Allergies = AppendText(record.Allergies, request.AllergiesToAppend);
        }

        if (!string.IsNullOrWhiteSpace(request.MedicalNotesToAppend))
        {
            record.MedicalNotes = AppendText(record.MedicalNotes, request.MedicalNotesToAppend);
        }

        record.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> ReplaceNotesAsync(int patientId, MedicalRecordUpdateNotesRequest request, CancellationToken cancellationToken = default)
    {
        var record = await GetTrackedAsync(patientId, cancellationToken);

        record.Allergies = string.IsNullOrWhiteSpace(request.Allergies) ? null : request.Allergies.Trim();
        record.MedicalNotes = string.IsNullOrWhiteSpace(request.MedicalNotes) ? null : request.MedicalNotes.Trim();
        record.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> AddEntryAsync(int patientId, MedicalRecordEntryInsertRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEntry(request.Treatment, request.Description);

        var record = await GetTrackedAsync(patientId, cancellationToken);
        var actingUserId = CurrentUserId(CurrentUser());

        _context.MedicalRecordEntries.Add(new MedicalRecordEntry
        {
            MedicalRecordId = record.Id,
            EntryDate = request.EntryDate,
            Treatment = request.Treatment.Trim(),
            Description = request.Description.Trim(),
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        });

        record.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(patientId, cancellationToken));
    }

    public async Task<MedicalRecordDto> UpdateEntryAsync(int entryId, MedicalRecordEntryUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEntry(request.Treatment, request.Description);

        var entry = await _context.MedicalRecordEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalRecordEntry), entryId);

        entry.EntryDate = request.EntryDate;
        entry.Treatment = request.Treatment.Trim();
        entry.Description = request.Description.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<MedicalRecordDto>(await LoadFullRecordAsync(
            await GetPatientIdForRecordAsync(entry.MedicalRecordId, cancellationToken), cancellationToken));
    }

    public async Task DeleteEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.MedicalRecordEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalRecordEntry), entryId);

        _context.MedicalRecordEntries.Remove(entry);
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

    private async Task<int> GetPatientIdForRecordAsync(int medicalRecordId, CancellationToken cancellationToken)
    {
        var record = await _context.MedicalRecords.SingleOrDefaultAsync(r => r.Id == medicalRecordId, cancellationToken)
            ?? throw new NotFoundException(nameof(MedicalRecord), medicalRecordId);
        return record.PatientId;
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
        var trimmedAddition = $"[{DateTime.UtcNow:dd.MM.yyyy}] {addition.Trim()}";
        return string.IsNullOrWhiteSpace(existing) ? trimmedAddition : $"{existing}\n{trimmedAddition}";
    }

    private static void ValidateEntry(string treatment, string description)
    {
        var errors = new Dictionary<string, string[]>();

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
