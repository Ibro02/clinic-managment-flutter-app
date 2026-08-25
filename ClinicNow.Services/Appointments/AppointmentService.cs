using System.Linq.Dynamic.Core;
using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Model.Messaging;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
// PagedResult is intentionally referenced fully-qualified at each usage site
// (ClinicNow.Model.Common.PagedResult<T>) despite the using above: this file also
// needs System.Linq.Dynamic.Core's OrderBy(string) extension, whose own
// PagedResult<T> would otherwise collide - same reasoning as BaseService.

namespace ClinicNow.Services.Appointments;

/// <summary>
/// Orchestrates the appointment lifecycle: resolves who's allowed to do what
/// (authorization + ownership - a Patient only ever touches their own
/// appointments, a Doctor only their own schedule), then hands the actual
/// transition to the state machine (<see cref="AppointmentStateMachine.BaseAppointmentState"/>),
/// which enforces whether the transition is legal and the business rules tied to
/// it. Doesn't extend <see cref="BaseService{TModel,TSearch,TDbEntity}"/> - unlike
/// every other entity, ownership here can't be expressed in the synchronous
/// <c>ApplyFilter</c> hook (it needs an async lookup of "which Patient/Doctor
/// row belongs to this JWT"), so the read side is implemented directly instead
/// of fighting that hook.
/// </summary>
public class AppointmentService : IAppointmentService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notificationService;
    private readonly IEmailPublisher _emailPublisher;

    public AppointmentService(
        ClinicNowContext context,
        IMapper mapper,
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notificationService,
        IEmailPublisher emailPublisher)
    {
        _context = context;
        _mapper = mapper;
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
        _emailPublisher = emailPublisher;
    }

    public async Task<ClinicNow.Model.Common.PagedResult<AppointmentDto>> GetPagedAsync(AppointmentSearchObject search, CancellationToken cancellationToken = default)
    {
        var query = ApplySearchFilters(search, IncludeAll(_context.Appointments.AsQueryable()));
        query = await ApplyOwnershipAsync(query, cancellationToken);

        var count = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search.OrderBy))
        {
            var direction = (search.SortDirection ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "desc" or "descending" => "descending",
                _ => "ascending"
            };
            try
            {
                query = query.OrderBy($"{search.OrderBy} {direction}");
            }
            catch (Exception)
            {
                query = query.OrderByDescending(a => a.StartUtc);
            }
        }
        else
        {
            query = query.OrderByDescending(a => a.StartUtc);
        }

        var entities = await query.Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);

        return new ClinicNow.Model.Common.PagedResult<AppointmentDto>
        {
            Count = count,
            ResultList = entities.Select(MapToDto).ToList()
        };
    }

    public async Task<AppointmentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var appointment = await IncludeAll(_context.Appointments.AsQueryable()).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return null;
        }

        await EnsureOwnershipAsync(appointment, cancellationToken);

        return MapToDto(appointment);
    }

    public async Task<AppointmentDto> ScheduleAsync(AppointmentInsertRequest request, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        var actingUserId = CurrentUserId(principal);

        int patientId;
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff))
        {
            if (request.PatientId is null)
            {
                throw new ValidationException("patientId", "Odabir pacijenta je obavezan.");
            }
            patientId = request.PatientId.Value;
        }
        else if (principal.IsInRole(Roles.Patient))
        {
            patientId = await GetOwnPatientIdAsync(actingUserId, cancellationToken);
        }
        else
        {
            throw new ForbiddenException("Nemate dozvolu za zakazivanje termina.");
        }

        var initialState = _serviceProvider.GetRequiredService<InitialAppointmentState>();
        var appointment = await initialState.ScheduleAsync(
            patientId, request.DoctorId, request.MedicalServiceId,
            request.StartUtc, actingUserId, cancellationToken);

        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        // Booking-event notification: the rulebook requires notifications for
        // "all relevant events" (booking, cancellation, status change, payment),
        // broader coverage than emails (confirm/cancel/payment only) - notify the
        // doctor that a new appointment landed on their schedule.
        await _notificationService.CreateAsync(
            reloaded.Doctor.UserId,
            "Novi termin zakazan",
            $"Pacijent {reloaded.Patient.FirstName} {reloaded.Patient.LastName} je zakazao/la termin za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC.",
            cancellationToken);

        return MapToDto(reloaded);
    }

    public async Task<AppointmentDto> ConfirmAsync(int id, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        if (!CanManageAppointments(principal))
        {
            throw new ForbiddenException("Nemate dozvolu za potvrđivanje termina.");
        }

        var appointment = await LoadTrackedAsync(id, cancellationToken);
        await EnsureDoctorOwnershipIfApplicableAsync(principal, appointment, cancellationToken);

        var state = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider);
        await state.ConfirmAsync(appointment, CurrentUserId(principal), cancellationToken);

        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        if (reloaded.Patient.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(
                patientUserId,
                "Termin potvrđen",
                $"Vaš termin kod dr. {reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je potvrđen.",
                cancellationToken);
        }

        if (reloaded.Patient.User is not null)
        {
            await _emailPublisher.PublishAsync(new EmailMessage
            {
                To = reloaded.Patient.User.Email,
                Subject = "ClinicNow - termin potvrđen",
                Body = $"Vaš termin kod dr. {reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je potvrđen."
            }, cancellationToken);
        }

        return MapToDto(reloaded);
    }

    public async Task<AppointmentDto> CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        if (!CanManageAppointments(principal))
        {
            throw new ForbiddenException("Nemate dozvolu za završavanje termina.");
        }

        var appointment = await LoadTrackedAsync(id, cancellationToken);
        await EnsureDoctorOwnershipIfApplicableAsync(principal, appointment, cancellationToken);

        var state = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider);
        await state.CompleteAsync(appointment, CurrentUserId(principal), cancellationToken);

        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        if (reloaded.Patient.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(
                patientUserId,
                "Termin završen",
                $"Vaš termin kod dr. {reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName} je označen kao završen.",
                cancellationToken);
        }

        return MapToDto(reloaded);
    }

    public async Task<AppointmentDto> CancelAsync(int id, AppointmentCancelRequest request, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        var actingUserId = CurrentUserId(principal);
        var appointment = await LoadTrackedAsync(id, cancellationToken);

        bool enforceCutoff;
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff))
        {
            enforceCutoff = false;
        }
        else if (principal.IsInRole(Roles.Doctor))
        {
            await EnsureDoctorOwnershipIfApplicableAsync(principal, appointment, cancellationToken);
            enforceCutoff = false;
        }
        else if (principal.IsInRole(Roles.Patient))
        {
            var ownPatientId = await GetOwnPatientIdAsync(actingUserId, cancellationToken);
            if (appointment.PatientId != ownPatientId)
            {
                throw new ForbiddenException("Ne možete otkazati tuđi termin.");
            }
            enforceCutoff = true; // patient-initiated: the 48h rule applies (rulebook §7)
        }
        else
        {
            throw new ForbiddenException("Nemate dozvolu za otkazivanje termina.");
        }

        var state = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider);
        await state.CancelAsync(appointment, actingUserId, request.Reason, enforceCutoff, cancellationToken);

        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        // Cancellation notifies both sides (rulebook Part II §G: rejection/
        // cancellation must trigger a notification with the reason).
        if (reloaded.Patient.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(
                patientUserId,
                "Termin otkazan",
                $"Vaš termin kod dr. {reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je otkazan. Razlog: {request.Reason}",
                cancellationToken);
        }

        await _notificationService.CreateAsync(
            reloaded.Doctor.UserId,
            "Termin otkazan",
            $"Termin sa pacijentom {reloaded.Patient.FirstName} {reloaded.Patient.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je otkazan. Razlog: {request.Reason}",
            cancellationToken);

        if (reloaded.Patient.User is not null)
        {
            await _emailPublisher.PublishAsync(new EmailMessage
            {
                To = reloaded.Patient.User.Email,
                Subject = "ClinicNow - termin otkazan",
                Body = $"Vaš termin kod dr. {reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je otkazan. Razlog: {request.Reason}"
            }, cancellationToken);
        }

        return MapToDto(reloaded);
    }

    public async Task<List<DateTime>> GetAvailableSlotsAsync(int doctorId, int medicalServiceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var medicalService = await _context.MedicalServices.FindAsync([medicalServiceId], cancellationToken)
            ?? throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");

        var workingHours = await _context.WorkingHoursEntries
            .Where(w => w.DoctorId == doctorId && w.DayOfWeek == date.DayOfWeek)
            .ToListAsync(cancellationToken);

        if (workingHours.Count == 0)
        {
            return [];
        }

        var dayStartUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var blocks = await _context.ScheduleBlocks
            .Where(b => b.DoctorId == doctorId && b.StartUtc < dayEndUtc && b.EndUtc > dayStartUtc)
            .ToListAsync(cancellationToken);

        var existing = await _context.Appointments
            .Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled &&
                        a.StartUtc < dayEndUtc && a.EndUtc > dayStartUtc)
            .Select(a => new { a.StartUtc, a.EndUtc })
            .ToListAsync(cancellationToken);

        var duration = TimeSpan.FromMinutes(medicalService.DurationMinutes);
        var now = DateTime.UtcNow;
        var slots = new List<DateTime>();

        foreach (var window in workingHours)
        {
            var slotStart = date.ToDateTime(window.StartTime, DateTimeKind.Utc);
            var windowEnd = date.ToDateTime(window.EndTime, DateTimeKind.Utc);

            while (slotStart + duration <= windowEnd)
            {
                var slotEnd = slotStart + duration;

                var isPast = slotStart <= now;
                var isBlocked = blocks.Any(b => b.StartUtc < slotEnd && b.EndUtc > slotStart);
                var isTaken = existing.Any(e => e.StartUtc < slotEnd && e.EndUtc > slotStart);

                if (!isPast && !isBlocked && !isTaken)
                {
                    slots.Add(slotStart);
                }

                slotStart = slotEnd; // back-to-back slots at service-duration granularity, no gaps
            }
        }

        return slots;
    }

    // --- helpers -----------------------------------------------------------------

    private static IQueryable<Appointment> IncludeAll(IQueryable<Appointment> query) => query
        .Include(a => a.Patient).ThenInclude(p => p!.User)
        .Include(a => a.Doctor).ThenInclude(d => d.User)
        .Include(a => a.MedicalService)
        .Include(a => a.Location);

    private static IQueryable<Appointment> ApplySearchFilters(AppointmentSearchObject search, IQueryable<Appointment> query)
    {
        if (search.PatientId.HasValue) query = query.Where(a => a.PatientId == search.PatientId.Value);
        if (search.DoctorId.HasValue) query = query.Where(a => a.DoctorId == search.DoctorId.Value);
        if (search.Status.HasValue) query = query.Where(a => a.Status == search.Status.Value);
        if (search.FromUtc.HasValue) query = query.Where(a => a.StartUtc >= search.FromUtc.Value);
        if (search.ToUtc.HasValue) query = query.Where(a => a.StartUtc <= search.ToUtc.Value);
        return query;
    }

    private async Task<IQueryable<Appointment>> ApplyOwnershipAsync(IQueryable<Appointment> query, CancellationToken cancellationToken)
    {
        var principal = CurrentUser();

        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff))
        {
            return query;
        }

        if (principal.IsInRole(Roles.Patient))
        {
            var patientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            return query.Where(a => a.PatientId == patientId);
        }

        if (principal.IsInRole(Roles.Doctor))
        {
            var doctorId = await GetOwnDoctorIdAsync(CurrentUserId(principal), cancellationToken);
            return query.Where(a => a.DoctorId == doctorId);
        }

        return query.Where(_ => false);
    }

    private async Task EnsureOwnershipAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var principal = CurrentUser();
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff)) return;

        if (principal.IsInRole(Roles.Patient))
        {
            var patientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
            if (appointment.PatientId != patientId) throw new ForbiddenException("Nemate pristup ovom terminu.");
            return;
        }

        if (principal.IsInRole(Roles.Doctor))
        {
            var doctorId = await GetOwnDoctorIdAsync(CurrentUserId(principal), cancellationToken);
            if (appointment.DoctorId != doctorId) throw new ForbiddenException("Nemate pristup ovom terminu.");
            return;
        }

        throw new ForbiddenException("Nemate pristup ovom terminu.");
    }

    private async Task EnsureDoctorOwnershipIfApplicableAsync(ClaimsPrincipal principal, Appointment appointment, CancellationToken cancellationToken)
    {
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff)) return;
        if (!principal.IsInRole(Roles.Doctor)) return;

        var doctorId = await GetOwnDoctorIdAsync(CurrentUserId(principal), cancellationToken);
        if (appointment.DoctorId != doctorId)
        {
            throw new ForbiddenException("Ne možete mijenjati termine drugog doktora.");
        }
    }

    private static bool CanManageAppointments(ClaimsPrincipal principal) =>
        principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff) || principal.IsInRole(Roles.Doctor);

    private async Task<int> GetOwnPatientIdAsync(int userId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");
        return patient.Id;
    }

    private async Task<int> GetOwnDoctorIdAsync(int userId, CancellationToken cancellationToken)
    {
        var doctor = await _context.Doctors.SingleOrDefaultAsync(d => d.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen doktorski profil za ovaj nalog.");
        return doctor.Id;
    }

    private async Task<Appointment> LoadTrackedAsync(int id, CancellationToken cancellationToken) =>
        await _context.Appointments.Include(a => a.AuditLogs).SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Appointment), id);

    private async Task<Appointment> ReloadAsync(int id, CancellationToken cancellationToken) =>
        await IncludeAll(_context.Appointments.AsQueryable()).SingleAsync(a => a.Id == id, cancellationToken);

    private AppointmentDto MapToDto(Appointment appointment)
    {
        var dto = _mapper.Map<AppointmentDto>(appointment);
        dto.AllowedActions = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider)
            .AllowedActions()
            .Select(a => a.ToString())
            .ToList();
        return dto;
    }

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
