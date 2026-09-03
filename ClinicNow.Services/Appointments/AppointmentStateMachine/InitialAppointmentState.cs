using System.Data;
using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
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
        var medicalService = await Context.MedicalServices.FindAsync([medicalServiceId], cancellationToken)
            ?? throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");

        // Doctor.LocationId, not a client-supplied field: a doctor practices at
        // exactly one clinic (1:1), so the appointment's location is always
        // derived from the chosen doctor, never picked independently.
        var doctor = await Context.Doctors.FindAsync([doctorId], cancellationToken)
            ?? throw new ValidationException("doctorId", "Odabrani doktor ne postoji.");
        var locationId = doctor.LocationId;

        if (!await Context.Patients.AnyAsync(p => p.Id == patientId, cancellationToken))
        {
            throw new ValidationException("patientId", "Odabrani pacijent ne postoji.");
        }

        // Both rows existing is not enough - the doctor must actually be qualified
        // for this service (review item C2). Enforced here, on the server, because
        // the client's dropdown filter is presentation and can be bypassed.
        if (!await DoctorCompatibility.CanPerformAsync(Context, doctorId, medicalService.SpecializationId, cancellationToken))
        {
            throw new ValidationException("medicalServiceId", DoctorCompatibility.NotQualifiedMessage);
        }

        if (startUtc <= DateTime.UtcNow)
        {
            throw new ValidationException("startUtc", "Termin mora biti zakazan u budućnosti.");
        }

        var endUtc = startUtc.AddMinutes(medicalService.DurationMinutes);

        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // WorkingHours stores the clinic's wall clock, so the requested instant has
        // to be projected into clinic-local time before it can be compared against
        // it - reading DayOfWeek/TimeOnly straight off the UTC instant booked
        // 08:00 Sarajevo as 08:00 UTC and looked up the wrong weekday near
        // midnight (review item C1).
        var dayOfWeek = ClinicTimeZone.LocalDayOfWeekOf(startUtc);
        var startTime = ClinicTimeZone.LocalTimeOf(startUtc);
        var endTime = ClinicTimeZone.LocalTimeOf(endUtc);

        var withinWorkingHours = await Context.WorkingHoursEntries.AnyAsync(w =>
            w.DoctorId == doctorId && w.DayOfWeek == dayOfWeek &&
            w.StartTime <= startTime && w.EndTime >= endTime, cancellationToken);
        if (!withinWorkingHours)
        {
            throw new BusinessException("Odabrani termin je izvan radnog vremena doktora.");
        }

        var isBlocked = await Context.ScheduleBlocks.AnyAsync(b =>
            b.DoctorId == doctorId && b.StartUtc < endUtc && b.EndUtc > startUtc, cancellationToken);
        if (isBlocked)
        {
            throw new BusinessException("Doktor nije dostupan u odabranom terminu (blokada rasporeda).");
        }

        var doctorOverlap = await Context.Appointments.AnyAsync(a =>
            a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled &&
            a.StartUtc < endUtc && a.EndUtc > startUtc, cancellationToken);
        if (doctorOverlap)
        {
            throw new BusinessException("Doktor već ima zakazan termin u odabranom periodu.");
        }

        var patientOverlap = await Context.Appointments.AnyAsync(a =>
            a.PatientId == patientId && a.Status != AppointmentStatus.Cancelled &&
            a.StartUtc < endUtc && a.EndUtc > startUtc, cancellationToken);
        if (patientOverlap)
        {
            throw new BusinessException("Pacijent već ima zakazan termin u odabranom periodu.");
        }

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
