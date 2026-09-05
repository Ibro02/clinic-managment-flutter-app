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

namespace ClinicNow.Services.Referrals;

public class ReferralService : IReferralService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReferralService(ClinicNowContext context, IMapper mapper, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagedResult<ReferralDto>> GetPagedAsync(ReferralSearchObject search, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();

        IQueryable<Referral> query = _context.Referrals
            .Include(r => r.Patient)
            .Include(r => r.ReferringDoctor).ThenInclude(d => d.User)
            .Include(r => r.SourceAppointment)
            .Include(r => r.TargetSpecialization);

        // The "Arhiva" view both Flutter clients show alongside the active
        // list deliberately bypasses the global soft-delete filter, same
        // pattern as PatientService's OnlyDeleted (review item C3).
        query = search.OnlyArchived
            ? query.IgnoreQueryFilters().Where(r => r.IsDeleted)
            : query;

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

        query = query.OrderByDescending(r => r.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);
        var entities = await query
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

        var referral = new Referral
        {
            PatientId = appointment.PatientId,
            ReferringDoctorId = appointment.DoctorId,
            SourceAppointmentId = appointment.Id,
            TargetSpecializationId = request.TargetSpecializationId,
            Reason = request.Reason.Trim(),
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Referrals.Add(referral);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.Referrals
            .Include(r => r.Patient)
            .Include(r => r.ReferringDoctor).ThenInclude(d => d.User)
            .Include(r => r.SourceAppointment)
            .Include(r => r.TargetSpecialization)
            .SingleAsync(r => r.Id == referral.Id, cancellationToken);

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
