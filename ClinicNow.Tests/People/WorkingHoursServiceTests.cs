using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.People;
using ClinicNow.Tests.TestSupport;

namespace ClinicNow.Tests.People;

/// <summary>
/// Review item C10: editing or deleting a doctor's working hours must not
/// silently strand an already-booked appointment outside the doctor's
/// availability, and multiple WorkingHours windows for the same doctor/day must
/// not illogically overlap. Built on the seeded Doctor 1 (Mon-Fri 08:00-16:00,
/// WorkingHours ids 1-5) via <see cref="TestContextFactory"/>.
/// </summary>
public class WorkingHoursServiceTests
{
    private const int Doctor1MondayId = 1;
    private const int Doctor1TuesdayId = 2;
    private const int Doctor1ThursdayId = 4;

    /// <summary>The next strictly-future Monday in clinic-local time, so a seeded appointment on it is never accidentally in the past.</summary>
    private static DateOnly NextMonday()
    {
        var today = DateOnly.FromDateTime(ClinicTimeZone.NowLocal);
        var daysAhead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysAhead == 0 ? 7 : daysAhead);
    }

    /// <summary>A Pending appointment for Doctor 1 at 09:00 clinic-local on the next Monday - inside the seeded 08:00-16:00 window.</summary>
    private static Appointment AddFutureMondayAppointment(ClinicNow.Services.Database.ClinicNowContext context, AppointmentStatus status = AppointmentStatus.Pending)
    {
        var startUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(9, 0));
        var appointment = new Appointment
        {
            PatientId = 1,
            DoctorId = 1,
            MedicalServiceId = 1,
            LocationId = 1,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(30),
            Status = status,
            CreatedByUserId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Appointments.Add(appointment);
        context.SaveChanges();
        return appointment;
    }

    // --- overlap between sibling WorkingHours rows --------------------------------

    [Fact]
    public async Task Insert_OverlappingAnotherWindowSameDay_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Doctor 1 already has Monday 08:00-16:00 (seeded id 1).
        var request = new WorkingHoursInsertRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) };

        await Assert.ThrowsAsync<ValidationException>(() => service.InsertAsync(request));
    }

    [Fact]
    public async Task Insert_NonOverlappingWindow_Succeeds()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Doctor 1 has no Saturday window at all.
        var request = new WorkingHoursInsertRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0) };

        var result = await service.InsertAsync(request);

        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task Update_MovedIntoOverlapWithAnotherWindowSameDay_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        var extra = await service.InsertAsync(new WorkingHoursInsertRequest
        { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(18, 0) });

        // Move it to overlap the seeded 08:00-16:00 Monday window (id 1).
        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(9, 0) };

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(extra.Id, request));
    }

    // --- stranding a future appointment via update ---------------------------------

    [Fact]
    public async Task Update_ShrinkingHoursPastAFutureAppointment_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context);
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Appointment is at 09:00-09:30 local; shrinking Monday to start at 10:00 strands it.
        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(Doctor1MondayId, request));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Update_ShrinkingHoursButStillCoveringTheAppointment_Succeeds()
    {
        await using var context = TestContextFactory.CreateContext();
        AddFutureMondayAppointment(context); // 09:00-09:30 local
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Narrower than 08:00-16:00, but 09:00-09:30 still fits inside 08:30-12:00.
        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(12, 0) };

        var result = await service.UpdateAsync(Doctor1MondayId, request);

        Assert.Equal(new TimeOnly(8, 30), result.StartTime);
    }

    [Fact]
    public async Task Update_WithNoFutureAppointments_AllowsShrinking()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Thursday (id 4) has no seeded appointment landing on it after "now".
        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) };

        var result = await service.UpdateAsync(Doctor1ThursdayId, request);

        Assert.Equal(new TimeOnly(12, 0), result.StartTime);
    }

    /// <summary>Moving a window to a different day must re-check the day it left, not just the day it moved to - the appointment loses coverage on the old day.</summary>
    [Fact]
    public async Task Update_MovingWindowOffTheAppointmentsDay_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context);
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        // Move the Monday window to Sunday - Doctor 1 has no other Monday coverage,
        // so the Monday appointment would be left outside every working-hours window.
        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(Doctor1MondayId, request));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Update_ConfirmedAppointmentAlsoCounts_NotJustPending()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context, AppointmentStatus.Confirmed);
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(Doctor1MondayId, request));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Update_CompletedAppointment_DoesNotBlockTheEdit()
    {
        await using var context = TestContextFactory.CreateContext();
        AddFutureMondayAppointment(context, AppointmentStatus.Completed); // already resolved - not a conflict
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        var request = new WorkingHoursUpdateRequest { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) };

        var result = await service.UpdateAsync(Doctor1MondayId, request);

        Assert.Equal(new TimeOnly(10, 0), result.StartTime);
    }

    // --- deleting a window entirely -------------------------------------------------

    [Fact]
    public async Task Delete_WindowCoveringAFutureAppointment_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context);
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(Doctor1MondayId));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Delete_WindowWithNoFutureAppointments_Succeeds()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = new WorkingHoursService(context, TestContextFactory.CreateMapper());

        await service.DeleteAsync(Doctor1TuesdayId);

        Assert.Null(await context.WorkingHoursEntries.FindAsync(Doctor1TuesdayId));
    }
}
