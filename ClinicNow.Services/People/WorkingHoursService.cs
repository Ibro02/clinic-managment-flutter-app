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
        await ValidateAsync(request.DoctorId, request.StartTime, request.EndTime, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(WorkingHoursUpdateRequest request, WorkingHours entity, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.DoctorId, request.StartTime, request.EndTime, cancellationToken);
    }

    private async Task ValidateAsync(int doctorId, TimeOnly start, TimeOnly end, CancellationToken cancellationToken)
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

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
