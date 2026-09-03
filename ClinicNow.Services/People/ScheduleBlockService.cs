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

public class ScheduleBlockService : BaseCRUDService<ScheduleBlockDto, ScheduleBlockSearchObject, ScheduleBlock, ScheduleBlockInsertRequest, ScheduleBlockUpdateRequest>
{
    public ScheduleBlockService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<ScheduleBlock> ApplyFilter(ScheduleBlockSearchObject search, IQueryable<ScheduleBlock> query)
    {
        query = query.Include(b => b.Doctor).ThenInclude(d => d.User);

        if (search.DoctorId.HasValue)
        {
            query = query.Where(b => b.DoctorId == search.DoctorId.Value);
        }

        return query;
    }

    public override async Task<ScheduleBlockDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.ScheduleBlocks
            .Include(b => b.Doctor).ThenInclude(d => d.User)
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<ScheduleBlockDto>(entity);
    }

    protected override Task AfterInsertAsync(ScheduleBlockInsertRequest request, ScheduleBlock entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(b => b.Doctor).Query().Include(d => d.User).LoadAsync(cancellationToken);

    protected override Task AfterUpdateAsync(ScheduleBlockUpdateRequest request, ScheduleBlock entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(b => b.Doctor).Query().Include(d => d.User).LoadAsync(cancellationToken);

    protected override async Task BeforeInsertAsync(ScheduleBlockInsertRequest request, ScheduleBlock entity, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.DoctorId, request.StartUtc, request.EndUtc, request.Reason, cancellationToken);
        await EnsureNoAppointmentConflictAsync(request.DoctorId, request.StartUtc, request.EndUtc, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(ScheduleBlockUpdateRequest request, ScheduleBlock entity, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.DoctorId, request.StartUtc, request.EndUtc, request.Reason, cancellationToken);
        await EnsureNoAppointmentConflictAsync(request.DoctorId, request.StartUtc, request.EndUtc, cancellationToken);
    }

    // No BeforeDeleteAsync guard: removing a block only ever restores
    // availability, it can never strand an appointment - unlike WorkingHours,
    // there is nothing here for a delete to protect against (review item C10).

    private async Task ValidateAsync(int doctorId, DateTime startUtc, DateTime endUtc, string reason, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (startUtc >= endUtc)
        {
            errors["endUtc"] = ["Vrijeme završetka mora biti nakon vremena početka."];
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            errors["reason"] = ["Razlog blokade je obavezan."];
        }

        var doctorExists = await Context.Doctors.AnyAsync(d => d.Id == doctorId, cancellationToken);
        if (!doctorExists)
        {
            errors["doctorId"] = ["Odabrani doktor ne postoji."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Rejects a block that would cover a future Pending/Confirmed appointment
    /// (review item C10) - staff must cancel or reschedule the appointment
    /// first, rather than the block silently leaving it active while the
    /// doctor is marked unavailable. Compared directly in UTC: both
    /// <see cref="ScheduleBlock"/> and <see cref="Appointment"/> already store
    /// real instants, unlike WorkingHours' wall-clock columns.
    /// </summary>
    private async Task EnsureNoAppointmentConflictAsync(int doctorId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var conflicting = await Context.Appointments
            .Where(a => a.DoctorId == doctorId
                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                && a.StartUtc > DateTime.UtcNow
                && a.StartUtc < endUtc && a.EndUtc > startUtc)
            .Select(a => new { a.Id, a.StartUtc })
            .ToListAsync(cancellationToken);

        if (conflicting.Count > 0)
        {
            var list = string.Join(", ", conflicting
                .OrderBy(a => a.StartUtc)
                .Select(a => $"#{a.Id} ({ClinicTimeZone.LocalDateOf(a.StartUtc):dd.MM.yyyy} {ClinicTimeZone.LocalTimeOf(a.StartUtc):HH:mm})"));
            throw new BusinessException(
                $"Blokada se preklapa sa {conflicting.Count} zakazan(im)/potvrđen(im) termin(om/ima): {list}. Prvo otkažite ili premjestite te termine.");
        }
    }
}
