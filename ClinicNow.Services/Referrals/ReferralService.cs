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

namespace ClinicNow.Services.Referrals;

public class ReferralService : IReferralService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notificationService;

    public ReferralService(
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

    public async Task<PagedResult<ReferralDto>> GetPagedAsync(ReferralSearchObject search, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        IQueryable<Referral> query = _context.Referrals
            .Include(r => r.Patient)
            .Include(r => r.ReferringDoctor).ThenInclude(d => d.User)
            .Include(r => r.SourceAppointment)
            .Include(r => r.TargetSpecialization)
            .Include(r => r.TargetDoctor).ThenInclude(d => d!.User);

        // "Arhiva" means "no longer available to book with" - either a real
        // soft-delete (Administrator's manual removal of a mistaken entry) or
        // simply already used to book an appointment, regardless of that
        // appointment's own status (requested directly by Ibrahim: waiting for
        // the resulting appointment to complete/cancel before moving a used
        // referral out of the active list just left two visually-identical
        // "active" states with nothing to tell them apart). Bypasses the
        // global soft-delete filter (same pattern as PatientService's
        // OnlyDeleted, review item C3) precisely because a used-but-not-yet-
        // soft-deleted referral must still surface here.
        query = search.OnlyArchived
            ? query.IgnoreQueryFilters().Where(r => r.IsDeleted || r.ResultingAppointmentId != null)
            : query.Where(r => r.ResultingAppointmentId == null);

        if (principal.IsInRole(Roles.Patient))
        {
            // A patient can never browse another patient's referrals,
            // regardless of what patientId the client sends - ownership is
            // always resolved from the JWT, never trusted from the query string.
            var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            query = query.Where(r => r.PatientId == ownPatientId);
        }
        else if (search.PatientId.HasValue)
        {
            query = query.Where(r => r.PatientId == search.PatientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(r => r.Reason.Contains(term) || r.TargetSpecialization.Name.Contains(term));
        }

        query = query.OrderByDescending(r => r.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);

        // AsNoTracking: a read path with four Includes, so tracking snapshots the
        // whole related graph per row for nothing. This service overrides
        // GetPagedAsync rather than inheriting BaseService's, so it does not pick
        // up that class's no-tracking read automatically.
        var entities = await query
            .AsNoTracking()
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ReferralDto>
        {
            Count = count,
            ResultList = _mapper.Map<List<ReferralDto>>(entities)
        };
    }

    public async Task<ReferralDto> CreateAsync(ReferralInsertRequest request, CancellationToken cancellationToken = default)
    {
        var actingUserId = CurrentUserId(CurrentUser());

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("reason", "Razlog upućivanja je obavezan.");
        }

        // The appointment is the single source of truth for which patient and
        // which referring doctor this referral belongs to - never trust
        // separate client-supplied ids that could disagree with it (same
        // reasoning as LabFindingService.CreateAsync, review item C4).
        var appointment = await _context.Appointments.SingleOrDefaultAsync(a => a.Id == request.SourceAppointmentId, cancellationToken)
            ?? throw new ValidationException("sourceAppointmentId", "Odabrani termin ne postoji.");

        var specializationExists = await _context.Specializations.AnyAsync(s => s.Id == request.TargetSpecializationId, cancellationToken);
        if (!specializationExists)
        {
            throw new ValidationException("targetSpecializationId", "Odabrana specijalizacija ne postoji.");
        }

        // Naming a specialist is optional, but naming one who does not hold the
        // requested specialization would produce a referral nobody could ever
        // book against - the same doctor↔specialization rule the booking itself
        // enforces (DoctorCompatibility), applied at the point the referral is
        // written rather than only when it is redeemed.
        if (request.TargetDoctorId.HasValue)
        {
            var qualified = await _context.DoctorSpecializations.AnyAsync(
                ds => ds.DoctorId == request.TargetDoctorId.Value
                    && ds.SpecializationId == request.TargetSpecializationId,
                cancellationToken);

            if (!qualified)
            {
                throw new ValidationException(
                    "targetDoctorId", "Odabrani doktor nema traženu specijalizaciju.");
            }
        }

        var referral = new Referral
        {
            PatientId = appointment.PatientId,
            ReferringDoctorId = appointment.DoctorId,
            SourceAppointmentId = appointment.Id,
            TargetSpecializationId = request.TargetSpecializationId,
            TargetDoctorId = request.TargetDoctorId,
            Reason = request.Reason.Trim(),
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Referrals.Add(referral);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.Referrals
            .Include(r => r.Patient).ThenInclude(p => p!.User)
            .Include(r => r.ReferringDoctor).ThenInclude(d => d.User)
            .Include(r => r.SourceAppointment)
            .Include(r => r.TargetSpecialization)
            .Include(r => r.TargetDoctor).ThenInclude(d => d!.User)
            .SingleAsync(r => r.Id == referral.Id, cancellationToken);

        // The prijava promises the patient is notified when a referral is
        // issued; rulebook §7.2 requires notifications for every relevant
        // event. Same nullable-User guard as LabFindingService: a
        // staff-created patient need not have a login account yet.
        if (reloaded.Patient?.User is not null)
        {
            var message = PatientMessages.ReferralIssued(
                reloaded.Patient.User.PreferredLanguage,
                reloaded.TargetSpecialization.Name,
                $"{reloaded.ReferringDoctor.User.FirstName} {reloaded.ReferringDoctor.User.LastName}");

            await _notificationService.CreateAsync(
                reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);
        }

        return _mapper.Map<ReferralDto>(reloaded);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var referral = await _context.Referrals.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Referral), id);

        referral.IsDeleted = true;
        referral.DeletedAtUtc = DateTime.UtcNow;
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
