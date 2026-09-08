using System.Linq.Dynamic.Core;
using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Localization;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Model.Messaging;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using ClinicNow.Services.Payments;
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
    private readonly IPaymentService _paymentService;

    public AppointmentService(
        ClinicNowContext context,
        IMapper mapper,
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notificationService,
        IEmailPublisher emailPublisher,
        IPaymentService paymentService)
    {
        _context = context;
        _mapper = mapper;
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
        _emailPublisher = emailPublisher;
        _paymentService = paymentService;
    }

    public async Task<ClinicNow.Model.Common.PagedResult<AppointmentDto>> GetPagedAsync(AppointmentSearchObject search, CancellationToken cancellationToken = default)
    {
        var query = ApplySearchFilters(search, IncludeAll(_context.Appointments.AsQueryable()));
        query = await ApplyOwnershipAsync(query, cancellationToken);

        var count = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search.OrderBy))
        {
            // Same closed allowlist BaseService enforces. Without it, OrderBy(string)
            // accepts any navigation path on the entity - `Doctor.User.PasswordHash`
            // included - and the resulting row order leaks that column's value one
            // comparison at a time. A rejected column is refused loudly rather than
            // silently ignored, so an invalid probe cannot be mistaken for a valid one.
            if (!search.IsSortable(search.OrderBy))
            {
                throw new ValidationException("orderBy",
                    $"Sortiranje po koloni '{search.OrderBy}' nije dozvoljeno.");
            }

            var direction = (search.SortDirection ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "desc" or "descending" => "descending",
                _ => "ascending"
            };
            // `, Id` is a tiebreaker, not decoration: IncludeAll is a split
            // query, and a split query pages each of its SQL statements
            // independently - so a non-unique ORDER BY (several appointments
            // share a StartUtc) could hand the collection queries a different
            // slice than the principal query got.
            query = query.OrderBy($"{search.OrderBy} {direction}, Id");
        }
        else
        {
            query = query.OrderByDescending(a => a.StartUtc).ThenBy(a => a.Id);
        }

        // AsNoTrackingWithIdentityResolution, not plain AsNoTracking: IncludeAll is
        // a split query, and without identity resolution the same Doctor/User row
        // arriving in two different result sets materializes as two distinct
        // instances. This is a read path, so tracking earns nothing - a 100-row page
        // otherwise snapshots ~700 entities into the change tracker for no reason.
        var entities = await query
            .AsNoTrackingWithIdentityResolution()
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return new ClinicNow.Model.Common.PagedResult<AppointmentDto>
        {
            Count = count,
            ResultList = entities.Select(a => MapToDto(a)).ToList()
        };
    }

    public async Task<AppointmentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        // AuditLogs is included only here, never in IncludeAll (used by the
        // paged list too) - the rulebook keeps list DTOs display-only, and this
        // is the one caller that needs the history (AppointmentDto.AuditLogs).
        var appointment = await IncludeAll(_context.Appointments.AsQueryable())
            .Include(a => a.AuditLogs).ThenInclude(l => l.ActingUser)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return null;
        }

        await EnsureOwnershipAsync(appointment, cancellationToken);

        return MapToDto(appointment, includeAuditLogs: true);
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

        // Validated before the booking itself so an invalid/already-used
        // referral fails fast rather than after an appointment is already
        // created (review item C5's "continue to booking" - the referral
        // must belong to this patient, and IgnoreQueryFilters is deliberately
        // NOT used here, so an archived referral simply doesn't match and is
        // rejected the same as a nonexistent one - "make sure archived
        // referrals can't be reused").
        Referral? referral = null;
        if (request.ReferralId.HasValue)
        {
            referral = await _context.Referrals.SingleOrDefaultAsync(r => r.Id == request.ReferralId.Value, cancellationToken)
                ?? throw new ValidationException("referralId", "Odabrana uputnica ne postoji.");

            if (referral.PatientId != patientId)
            {
                throw new ForbiddenException("Ne možete koristiti tuđu uputnicu.");
            }

            if (referral.ResultingAppointmentId is not null)
            {
                throw new BusinessException("Ova uputnica je već iskorištena za zakazivanje termina.");
            }
        }

        // Some services (e.g. a surgical consultation) can't be self-booked
        // without first being referred to them. Checked here, before the
        // transactional booking itself, alongside the referral validation
        // above it depends on - a missing MedicalServiceId is left for
        // EnsureAvailableAsync's own "usluga ne postoji" check further down.
        var medicalService = await _context.MedicalServices.FindAsync([request.MedicalServiceId], cancellationToken);
        if (medicalService is { IsReferralRequired: true })
        {
            if (referral is null)
            {
                throw new ValidationException("referralId", "Za zakazivanje ove usluge potrebna je aktivna uputnica.");
            }

            if (referral.TargetSpecializationId != medicalService.SpecializationId)
            {
                throw new ValidationException("referralId", "Uputnica ne odgovara specijalizaciji odabrane usluge.");
            }
        }

        var initialState = _serviceProvider.GetRequiredService<InitialAppointmentState>();
        // The referral goes in with the booking rather than being linked afterwards,
        // so both land in the state machine's single Serializable transaction. A
        // second SaveChanges out here could fail after the appointment committed,
        // leaving the referral unused and therefore redeemable a second time.
        var appointment = await initialState.ScheduleAsync(
            patientId, request.DoctorId, request.MedicalServiceId,
            request.StartUtc, actingUserId, cancellationToken, referral);

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

        if (reloaded.Patient.User is not null)
        {
            // Review item 7: rendered in the patient's own PreferredLanguage,
            // not hard-coded Bosnian - the same LocalizedMessage backs both
            // the in-app notification and the email so the two never drift.
            var message = PatientMessages.AppointmentConfirmed(
                reloaded.Patient.User.PreferredLanguage, $"{reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName}", reloaded.StartUtc);

            await _notificationService.CreateAsync(reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);

            await _emailPublisher.PublishAsync(new EmailMessage
            {
                To = reloaded.Patient.User.Email,
                Subject = $"ClinicNow - {message.Title}",
                Body = message.Body
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

        if (reloaded.Patient.User is not null)
        {
            var message = PatientMessages.AppointmentCompleted(
                reloaded.Patient.User.PreferredLanguage, $"{reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName}");
            await _notificationService.CreateAsync(reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);
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

        // Auto-refund the remaining balance on any cancellation of a paid
        // appointment (design doc §2/§4 item 4) - never throws, so a PayPal
        // failure here can't undo the cancellation that already succeeded.
        await _paymentService.RefundForCancelledAppointmentAsync(appointment.Id, actingUserId, cancellationToken);

        // Reloaded *after* the refund attempt, deliberately (review item C14).
        // Reading the appointment first meant the response described the money
        // as it was before the refund ran - so a caller was told "cancelled"
        // with no hint that the refund had failed and the money was stuck, and
        // even a successful refund went unreflected until the next fetch.
        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        // Cancellation notifies both sides (rulebook Part II §G: rejection/
        // cancellation must trigger a notification with the reason). Only the
        // patient's own copy is localized via PreferredLanguage (review item
        // 7) - the doctor notification below stays Bosnian regardless.
        if (reloaded.Patient.User is not null)
        {
            var message = PatientMessages.AppointmentCancelled(
                reloaded.Patient.User.PreferredLanguage, $"{reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName}",
                reloaded.StartUtc, request.Reason);

            await _notificationService.CreateAsync(reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);

            await _emailPublisher.PublishAsync(new EmailMessage
            {
                To = reloaded.Patient.User.Email,
                Subject = $"ClinicNow - {message.Title}",
                Body = message.Body
            }, cancellationToken);
        }

        await _notificationService.CreateAsync(
            reloaded.Doctor.UserId,
            "Termin otkazan",
            $"Termin sa pacijentom {reloaded.Patient.FirstName} {reloaded.Patient.LastName} za {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC je otkazan. Razlog: {request.Reason}",
            cancellationToken);

        return MapToDto(reloaded);
    }

    public async Task<AppointmentDto> RescheduleAsync(int id, AppointmentRescheduleRequest request, CancellationToken cancellationToken = default)
    {
        var principal = CurrentUser();
        var actingUserId = CurrentUserId(principal);
        var appointment = await LoadTrackedAsync(id, cancellationToken);

        // Same role/ownership shape as CancelAsync (review item C6: "a rule for
        // who may reschedule") - a move is, from the doctor's schedule's point of
        // view, exactly as disruptive on short notice as a cancellation, so it
        // gets the same 48h patient cutoff.
        bool enforceCutoff;
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff))
        {
            enforceCutoff = false;
        }
        else if (principal.IsInRole(Roles.Doctor))
        {
            await EnsureDoctorOwnershipIfApplicableAsync(principal, appointment, cancellationToken);

            // A doctor may move their own appointment in time, but not hand it
            // to a colleague: that would commit another doctor to work they
            // never agreed to take. The ownership check above only proves the
            // appointment is theirs *now*, which says nothing about where it is
            // going, so the target is checked separately. Administrator/Staff
            // reassign freely (the branch above) - scheduling other people's
            // work is their job.
            if (request.DoctorId != appointment.DoctorId)
            {
                throw new ForbiddenException(
                    "Termin možete premjestiti u drugo vrijeme, ali ne i na drugog doktora. Za promjenu doktora obratite se osoblju klinike.");
            }

            enforceCutoff = false;
        }
        else if (principal.IsInRole(Roles.Patient))
        {
            var ownPatientId = await GetOwnPatientIdAsync(actingUserId, cancellationToken);
            if (appointment.PatientId != ownPatientId)
            {
                throw new ForbiddenException("Ne možete premjestiti tuđi termin.");
            }
            enforceCutoff = true;
        }
        else
        {
            throw new ForbiddenException("Nemate dozvolu za premještanje termina.");
        }

        var state = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider);
        await state.RescheduleAsync(appointment, request.DoctorId, request.StartUtc, actingUserId, enforceCutoff, cancellationToken);

        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        // A moved appointment is a status change (back to Pending) affecting
        // both sides, exactly the kind of event the rulebook requires a
        // notification for (Part II §G) - same shape as Schedule/Confirm/Cancel.
        // The doctor-facing copy stays Bosnian (review item 7's language
        // preference is patient-only).
        if (reloaded.Patient.User is not null)
        {
            var message = PatientMessages.AppointmentRescheduled(
                reloaded.Patient.User.PreferredLanguage, $"{reloaded.Doctor.User.FirstName} {reloaded.Doctor.User.LastName}", reloaded.StartUtc);

            await _notificationService.CreateAsync(reloaded.Patient.User.Id, message.Title, message.Body, cancellationToken);

            await _emailPublisher.PublishAsync(new EmailMessage
            {
                To = reloaded.Patient.User.Email,
                Subject = $"ClinicNow - {message.Title}",
                Body = message.Body
            }, cancellationToken);
        }

        await _notificationService.CreateAsync(
            reloaded.Doctor.UserId,
            "Termin premješten",
            $"Termin sa pacijentom {reloaded.Patient.FirstName} {reloaded.Patient.LastName} je premješten na {reloaded.StartUtc:dd.MM.yyyy HH:mm} UTC.",
            cancellationToken);

        return MapToDto(reloaded);
    }

    public async Task<List<DateTime>> GetAvailableSlotsAsync(int doctorId, int medicalServiceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var medicalService = await _context.MedicalServices.FindAsync([medicalServiceId], cancellationToken)
            ?? throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");

        // No slots at all for a pairing the booking endpoint would reject anyway
        // (review item C2) - offering them would just move the error to the click.
        if (!await DoctorCompatibility.CanPerformAsync(_context, doctorId, medicalService.SpecializationId, cancellationToken))
        {
            return [];
        }

        var workingHours = await _context.WorkingHoursEntries
            .Where(w => w.DoctorId == doctorId && w.DayOfWeek == date.DayOfWeek)
            .ToListAsync(cancellationToken);

        if (workingHours.Count == 0)
        {
            return [];
        }

        // `date` is the clinic-local calendar day the patient picked, and
        // WorkingHours holds clinic wall-clock times - so both the day bounds and
        // the window edges below must be resolved through ClinicTimeZone, never
        // stamped as UTC (review item C1). LocalDateEndExclusiveUtc also keeps the
        // 23h/25h DST days correct, which `dayStartUtc.AddDays(1)` did not.
        var dayStartUtc = ClinicTimeZone.LocalDateStartUtc(date);
        var dayEndUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(date);

        var blocks = await _context.ScheduleBlocks
            .Where(b => b.DoctorId == doctorId && b.StartUtc < dayEndUtc && b.EndUtc > dayStartUtc)
            .ToListAsync(cancellationToken);

        var existing = await _context.Appointments
            .Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled &&
                        a.StartUtc < dayEndUtc && a.EndUtc > dayStartUtc)
            .Select(a => new { a.StartUtc, a.EndUtc })
            .ToListAsync(cancellationToken);

        // The walk itself lives in SlotGeneration so the recommender can run the
        // exact same rule over a batch-loaded window instead of calling this
        // method once per doctor/service/day (review item C19).
        return SlotGeneration.FreeSlots(
            date,
            workingHours,
            blocks.Select(b => new SlotGeneration.Interval(b.StartUtc, b.EndUtc)).ToList(),
            existing.Select(e => new SlotGeneration.Interval(e.StartUtc, e.EndUtc)).ToList(),
            TimeSpan.FromMinutes(medicalService.DurationMinutes),
            DateTime.UtcNow);
    }

    // --- helpers -----------------------------------------------------------------

    private static IQueryable<Appointment> IncludeAll(IQueryable<Appointment> query) => query
        .Include(a => a.Patient).ThenInclude(p => p!.User)
        .Include(a => a.Doctor).ThenInclude(d => d.User)
        .Include(a => a.MedicalService)
        .Include(a => a.Location)
        .Include(a => a.Payments).ThenInclude(p => p.Refunds)
        // Two nested collection includes (Payments -> Refunds) alongside the
        // reference includes above would otherwise be one JOIN, multiplying
        // every appointment row by (payments x refunds) - a cartesian explosion
        // paid for on every page of the list endpoint.
        .AsSplitQuery();

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

    /// <summary>
    /// Whether <paramref name="principal"/> may actually perform <paramref name="action"/>
    /// on <paramref name="appointment"/> right now - the same role/time rules
    /// <see cref="ConfirmAsync"/>, <see cref="CompleteAsync"/> and
    /// <see cref="CancelAsync"/> already enforce, mirrored here so
    /// <c>AllowedActions</c> never offers a button the server would reject on
    /// click (review item C9).
    /// </summary>
    private static bool IsActuallyAllowed(AppointmentAction action, Appointment appointment, ClaimsPrincipal principal) => action switch
    {
        // Same role gate as ConfirmAsync/CompleteAsync, plus the timing rule that
        // is otherwise only enforced when the click actually happens.
        AppointmentAction.Confirm => CanManageAppointments(principal) && !BaseAppointmentState.HasStarted(appointment),
        AppointmentAction.Complete => CanManageAppointments(principal) && BaseAppointmentState.HasStarted(appointment),
        // Same role gate as CancelAsync: Administrator/Staff/Doctor cancel without
        // a cutoff; a Patient is subject to the 48h rule.
        AppointmentAction.Cancel => CanManageAppointments(principal)
            || (principal.IsInRole(Roles.Patient) && !BaseAppointmentState.IsWithinCancellationCutoff(appointment)),
        // Same role gate and cutoff as Cancel - RescheduleAsync enforces the
        // identical rule (review item C6).
        AppointmentAction.Reschedule => CanManageAppointments(principal)
            || (principal.IsInRole(Roles.Patient) && !BaseAppointmentState.IsWithinCancellationCutoff(appointment)),
        _ => false
    };

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

    /// <summary>
    /// Loads the appointment tracked, with every navigation <see cref="MapToDto"/>
    /// will need already attached.
    ///
    /// Pulling the includes in here rather than re-querying afterwards is what lets
    /// <see cref="ReloadAsync"/> become a no-op on the second call: a mutation used
    /// to run this query and then the full IncludeAll split query again, six
    /// round-trips to read one appointment twice.
    /// </summary>
    private async Task<Appointment> LoadTrackedAsync(int id, CancellationToken cancellationToken) =>
        await IncludeAll(_context.Appointments.AsQueryable())
            .Include(a => a.AuditLogs)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Appointment), id);

    /// <summary>
    /// Returns the appointment with all navigations loaded, reusing the tracked
    /// instance when the change tracker already holds one.
    ///
    /// After a state transition the tracked entity is already current - it *is* what
    /// was just written - so the only work needed is making sure the navigations are
    /// populated. Callers that reach here without a tracked instance (the
    /// post-Schedule path, where the appointment was inserted by the state machine)
    /// still get the full query.
    /// </summary>
    private async Task<Appointment> ReloadAsync(int id, CancellationToken cancellationToken)
    {
        var tracked = _context.ChangeTracker.Entries<Appointment>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(a => a.Id == id);

        if (tracked is null || tracked.Patient is null || tracked.Doctor is null
            || tracked.MedicalService is null || tracked.Location is null)
        {
            return await IncludeAll(_context.Appointments.AsQueryable()).SingleAsync(a => a.Id == id, cancellationToken);
        }

        // The people/service/location navigations cannot have changed under a state
        // transition, but the money can: CancelAsync deliberately reloads *after*
        // attempting a refund (review item C14) so the response reflects what
        // actually happened to the payment. Re-reading just that collection keeps
        // that guarantee while still skipping the rest of the split query.
        await _context.Entry(tracked)
            .Collection(a => a.Payments)
            .Query()
            .Include(p => p.Refunds)
            .LoadAsync(cancellationToken);

        return tracked;
    }

    /// <summary>
    /// Per-request memo of "which actions are structurally legal from this status".
    /// <see cref="MapToDto"/> runs once per row, and resolving a state class out of
    /// <see cref="IServiceProvider"/> for every row means up to <c>PageSize</c>
    /// service-locator lookups per request to answer at most five distinct
    /// questions. The service is registered <c>Scoped</c>, so an instance field is
    /// scoped to one request and needs no further synchronization.
    /// </summary>
    private readonly Dictionary<AppointmentStatus, List<AppointmentAction>> _structurallyAllowedByStatus = [];

    /// <summary>Cached for the same reason and with the same lifetime as <see cref="_structurallyAllowedByStatus"/>.</summary>
    private ClaimsPrincipal? _currentUserForRequest;

    private List<AppointmentAction> StructurallyAllowed(AppointmentStatus status)
    {
        if (!_structurallyAllowedByStatus.TryGetValue(status, out var actions))
        {
            actions = BaseAppointmentState.CreateState(status, _serviceProvider).AllowedActions().ToList();
            _structurallyAllowedByStatus[status] = actions;
        }

        return actions;
    }

    private AppointmentDto MapToDto(Appointment appointment, bool includeAuditLogs = false)
    {
        var dto = _mapper.Map<AppointmentDto>(appointment);

        // The state class only knows "legal from this status" - actually being
        // allowed also depends on who's asking and what time it is (review item
        // C9: ScheduledAppointmentState listed Confirm as allowed even for an
        // appointment whose time had passed, and AllowedActions never considered
        // role or the 48h cutoff). By the time an appointment reaches this method,
        // ApplyOwnershipAsync/EnsureOwnershipAsync/EnsureDoctorOwnershipIfApplicableAsync
        // have already restricted a Doctor/Patient caller to their own
        // appointments, so no further ownership check is needed here - only role
        // and timing.
        var principal = CurrentUser();
        var structurallyAllowed = StructurallyAllowed(appointment.Status);
        dto.AllowedActions = structurallyAllowed
            .Where(action => IsActuallyAllowed(action, appointment, principal))
            .Select(a => a.ToString())
            .ToList();

        // Priced from the same column, through the same converter, that
        // PaymentService.CreateAsync uses - so the sum a patient confirms in
        // the client is by construction the sum the server will order from
        // PayPal, rather than a second opinion the two could disagree on.
        // MedicalService is always loaded here: every caller reaches MapToDto
        // through IncludeAll.
        dto.PriceKm = appointment.MedicalService.Price;
        dto.PayableAmountEur = CurrencyConverter.ConvertKmToEur(appointment.MedicalService.Price);

        // Cancelled is excluded alongside Pending (review item C12): an
        // abandoned attempt is not this appointment's payment, and since
        // IsPaid below is "anything that isn't Refunded", letting one through
        // here would report an appointment nobody paid for as paid.
        var currentPayment = appointment.Payments
            .Where(p => p.Status != Model.Common.PaymentStatus.Pending
                && p.Status != Model.Common.PaymentStatus.Cancelled)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefault();

        if (currentPayment is not null)
        {
            dto.PaymentId = currentPayment.Id;
            dto.PaymentStatus = currentPayment.Status.ToDisplayName();
            dto.IsPaid = currentPayment.Status != Model.Common.PaymentStatus.Refunded;
            // Captured, not ordered, and RequiresReconciliation is refundable
            // too (review item C13a) - a mismatched capture is money that may
            // well need giving back, so staff must not be locked out of it.
            var remaining = (currentPayment.CapturedAmountEur ?? currentPayment.AmountEur) - currentPayment.Refunds.Sum(r => r.AmountEur);
            dto.CanRefund = currentPayment.Status is Model.Common.PaymentStatus.Paid
                    or Model.Common.PaymentStatus.PartiallyRefunded
                    or Model.Common.PaymentStatus.RequiresReconciliation
                && remaining > 0;
            dto.RefundFailed = currentPayment.RefundFailedAtUtc is not null;
        }

        if (includeAuditLogs)
        {
            dto.AuditLogs = appointment.AuditLogs
                .OrderByDescending(l => l.OccurredAtUtc)
                .Select(l => new AppointmentAuditLogDto
                {
                    Id = l.Id,
                    ActingUserName = l.ActingUser.FirstName + " " + l.ActingUser.LastName,
                    Status = l.Status,
                    StatusName = l.Status.ToDisplayName(),
                    OccurredAtUtc = l.OccurredAtUtc,
                    Description = l.Description
                })
                .ToList();
        }

        return dto;
    }

    private ClaimsPrincipal CurrentUser() =>
        _currentUserForRequest ??= _httpContextAccessor.HttpContext?.User
            ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
