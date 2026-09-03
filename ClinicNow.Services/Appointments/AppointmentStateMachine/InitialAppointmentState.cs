using System.Data;
using ClinicNow.Model.Common;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

/// <summary>
/// The "state" a booking is in before any <see cref="Appointment"/> row exists.
/// Not resolved via <see cref="BaseAppointmentState.CreateState"/> (there's no
/// status to look up yet) - <see cref="AppointmentService"/> asks for this one
/// directly by type whenever it needs to create a booking.
/// </summary>
public class InitialAppointmentState : BaseAppointmentState
{
    public InitialAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    /// <summary>
    /// Validates availability (working hours, blocks, doctor/patient overlap)
    /// and persists a new <c>Pending</c> appointment - all inside a Serializable
    /// transaction, so the classic "two requests both read no-overlap, both
    /// insert" race can't produce a double-booking (rulebook: "no double-booking,
    /// even under concurrent requests" - a plain read-then-write at the default
    /// isolation level cannot guarantee that; range locks under Serializable can).
    /// </summary>
    public async Task<Appointment> ScheduleAsync(
        int patientId, int doctorId, int medicalServiceId,
        DateTime startUtc, int actingUserId, CancellationToken cancellationToken)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Shared with RescheduleCoreAsync (review item C6) - one definition of
        // "is this slot actually available" for both a new booking and a move.
        var (endUtc, locationId) = await EnsureAvailableAsync(
            patientId, doctorId, medicalServiceId, startUtc, excludeAppointmentId: null, cancellationToken);

        var appointment = new Appointment
        {
            PatientId = patientId,
            DoctorId = doctorId,
            MedicalServiceId = medicalServiceId,
            LocationId = locationId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        AddAuditLog(appointment, AppointmentStatus.Pending, actingUserId, "Termin zakazan.");

        Context.Appointments.Add(appointment);
        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return appointment;
    }
}
