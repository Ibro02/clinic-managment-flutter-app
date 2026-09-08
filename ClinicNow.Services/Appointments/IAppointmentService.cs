using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.Appointments;

/// <summary>
/// Appointment doesn't fit the generic CRUD shape (no client-driven Update, no
/// Delete at all - status only ever changes through the state machine, and rows
/// are never removed), so this is a bespoke interface rather than
/// <c>ICRUDService&lt;...&gt;</c> - it still extends <see cref="IService{TModel,TSearch}"/>
/// for the read side, reused as-is by <c>BaseController</c>.
/// </summary>
public interface IAppointmentService : IService<AppointmentDto, AppointmentSearchObject>
{
    Task<AppointmentDto> ScheduleAsync(AppointmentInsertRequest request, CancellationToken cancellationToken = default);

    Task<AppointmentDto> ConfirmAsync(int id, CancellationToken cancellationToken = default);

    Task<AppointmentDto> CompleteAsync(int id, CancellationToken cancellationToken = default);

    Task<AppointmentDto> CancelAsync(int id, AppointmentCancelRequest request, CancellationToken cancellationToken = default);

    Task<AppointmentDto> RescheduleAsync(int id, AppointmentRescheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Real, currently-free start times for a doctor+service on a given day - never a client-computed guess (rulebook §7: "only real free slots offered").</summary>
    Task<List<DateTime>> GetAvailableSlotsAsync(int doctorId, int medicalServiceId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <c>AppointmentStatus</c> with its description and the transitions
    /// legal from it right now, for the read-only "Statusi termina" codebook tab
    /// (review item S2). No DB access - a pure read of the state machine, so it's
    /// synchronous rather than faking an async signature.
    /// </summary>
    IReadOnlyList<AppointmentStatusInfoDto> GetStatusInfo();
}
