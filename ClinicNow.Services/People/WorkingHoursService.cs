using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.People;

public class WorkingHoursService : BaseCRUDService<WorkingHoursDto, WorkingHoursSearchObject, WorkingHours, WorkingHoursInsertRequest, WorkingHoursUpdateRequest>
{
    public WorkingHoursService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<WorkingHours> ApplyFilter(WorkingHoursSearchObject search, IQueryable<WorkingHours> query)
    {
        query = query.Include(w => w.Doctor).ThenInclude(d => d.User);

        if (search.DoctorId.HasValue)
        {
            query = query.Where(w => w.DoctorId == search.DoctorId.Value);
        }

        return query;
    }

    public override async Task<WorkingHoursDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.WorkingHoursEntries
            .Include(w => w.Doctor).ThenInclude(d => d.User)
            .SingleOrDefaultAsync(w => w.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<WorkingHoursDto>(entity);
    }

    protected override Task AfterInsertAsync(WorkingHoursInsertRequest request, WorkingHours entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(w => w.Doctor).Query().Include(d => d.User).LoadAsync(cancellationToken);

    protected override Task AfterUpdateAsync(WorkingHoursUpdateRequest request, WorkingHours entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(w => w.Doctor).Query().Include(d => d.User).LoadAsync(cancellationToken);

    protected override async Task BeforeInsertAsync(WorkingHoursInsertRequest request, WorkingHours entity, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.DoctorId, request.DayOfWeek, request.StartTime, request.EndTime, excludeId: null, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(WorkingHoursUpdateRequest request, WorkingHours entity, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.DoctorId, request.DayOfWeek, request.StartTime, request.EndTime, excludeId: entity.Id, cancellationToken);

        // Both the day this row now covers and (if it moved) the day it used to
        // cover need re-checking - an appointment on the old day loses this row's
        // coverage even though the row itself didn't shrink (review item C10).
        var affectedDays = entity.DayOfWeek == request.DayOfWeek
            ? new[] { request.DayOfWeek }
            : new[] { request.DayOfWeek, entity.DayOfWeek };

        await EnsureNoStrandedAppointmentsAsync(
            entity.DoctorId, entity.Id, affectedDays,
            (request.DayOfWeek, request.StartTime, request.EndTime), cancellationToken);
    }

    protected override async Task BeforeDeleteAsync(WorkingHours entity, CancellationToken cancellationToken)
    {
        // Deleting the row removes its coverage entirely - no replacement window
        // to add, unlike an update.
        await EnsureNoStrandedAppointmentsAsync(entity.DoctorId, entity.Id, [entity.DayOfWeek], proposedWindow: null, cancellationToken);
    }

    private async Task ValidateAsync(int doctorId, DayOfWeek dayOfWeek, TimeOnly start, TimeOnly end, int? excludeId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (start >= end)
        {
            errors["endTime"] = ["Vrijeme završetka mora biti nakon vremena početka."];
        }

        var doctorExists = await Context.Doctors.AnyAsync(d => d.Id == doctorId, cancellationToken);
        if (!doctorExists)
        {
            errors["doctorId"] = ["Odabrani doktor ne postoji."];
        }

        // Only meaningful once the window itself and the doctor are valid -
        // otherwise this query would run (and could "pass") on garbage input.
        if (doctorExists && start < end)
        {
            var overlapsAnotherWindow = await Context.WorkingHoursEntries.AnyAsync(w =>
                w.DoctorId == doctorId && w.DayOfWeek == dayOfWeek && w.Id != (excludeId ?? 0) &&
                w.StartTime < end && w.EndTime > start, cancellationToken);
            if (overlapsAnotherWindow)
            {
                errors["startTime"] = ["Radno vrijeme se preklapa sa već postojećim rasporedom za ovaj dan."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Rejects an edit or delete of a WorkingHours row if it would leave any
    /// future Pending/Confirmed appointment outside every remaining
    /// working-hours window for its day (review item C10). Appointments are
    /// compared in clinic-local time (review item C1), since WorkingHours
    /// stores clinic wall-clock values.
    /// </summary>
    /// <param name="excludeWorkingHoursId">The row being edited/deleted - excluded from "remaining windows".</param>
    /// <param name="affectedDays">Every day whose coverage could have changed.</param>
    /// <param name="proposedWindow">The edited row's new (day, start, end) - <c>null</c> on delete, where no replacement window exists.</param>
    private async Task EnsureNoStrandedAppointmentsAsync(
        int doctorId, int excludeWorkingHoursId, IReadOnlyCollection<DayOfWeek> affectedDays,
        (DayOfWeek Day, TimeOnly Start, TimeOnly End)? proposedWindow, CancellationToken cancellationToken)
    {
        var remainingWindows = await Context.WorkingHoursEntries
            .Where(w => w.DoctorId == doctorId && affectedDays.Contains(w.DayOfWeek) && w.Id != excludeWorkingHoursId)
            .Select(w => new { w.DayOfWeek, w.StartTime, w.EndTime })
            .ToListAsync(cancellationToken);

        var windows = proposedWindow is { } proposed
            ? remainingWindows.Append(new { DayOfWeek = proposed.Day, StartTime = proposed.Start, EndTime = proposed.End }).ToList()
            : remainingWindows;

        var futureActiveAppointments = await Context.Appointments
            .Where(a => a.DoctorId == doctorId
                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                && a.StartUtc > DateTime.UtcNow)
            .Select(a => new { a.Id, a.StartUtc, a.EndUtc })
            .ToListAsync(cancellationToken);

        var stranded = futureActiveAppointments.Where(a =>
        {
            var day = ClinicTimeZone.LocalDayOfWeekOf(a.StartUtc);
            if (!affectedDays.Contains(day))
            {
                return false;
            }

            var start = ClinicTimeZone.LocalTimeOf(a.StartUtc);
            var end = ClinicTimeZone.LocalTimeOf(a.EndUtc);
            return !windows.Any(w => w.DayOfWeek == day && w.StartTime <= start && w.EndTime >= end);
        }).ToList();

        if (stranded.Count > 0)
        {
            var list = string.Join(", ", stranded
                .OrderBy(a => a.StartUtc)
                .Select(a => $"#{a.Id} ({ClinicTimeZone.LocalDateOf(a.StartUtc):dd.MM.yyyy} {ClinicTimeZone.LocalTimeOf(a.StartUtc):HH:mm})"));
            throw new BusinessException(
                $"Izmjena radnog vremena bi ostavila {stranded.Count} zakazan(ih)/potvrđen(ih) termin(a) van radnog vremena doktora: {list}. Prvo otkažite ili premjestite te termine.");
        }
    }
}
