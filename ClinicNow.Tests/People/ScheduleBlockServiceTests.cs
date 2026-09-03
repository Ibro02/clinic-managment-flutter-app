using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.People;
using ClinicNow.Tests.TestSupport;

namespace ClinicNow.Tests.People;

/// <summary>
/// Review item C10: a schedule block (vacation, meeting) must not be placeable
/// over an already-booked appointment - staff would otherwise mark a doctor
/// unavailable while the system still thinks the appointment is on.
/// </summary>
public class ScheduleBlockServiceTests
{
    private static DateOnly NextMonday()
    {
        var today = DateOnly.FromDateTime(ClinicTimeZone.NowLocal);
        var daysAhead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysAhead == 0 ? 7 : daysAhead);
    }

    private static Appointment AddFutureMondayAppointment(ClinicNow.Services.Database.ClinicNowContext context)
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
            Status = AppointmentStatus.Confirmed,
            CreatedByUserId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Appointments.Add(appointment);
        context.SaveChanges();
        return appointment;
    }

    [Fact]
    public async Task Insert_OverlappingAFutureAppointment_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context); // 09:00-09:30 local
        var service = new ScheduleBlockService(context, TestContextFactory.CreateMapper());

        var startUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(8, 0));
        var request = new ScheduleBlockInsertRequest
        { DoctorId = 1, StartUtc = startUtc, EndUtc = startUtc.AddHours(4), Reason = "Godišnji odmor" };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.InsertAsync(request));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Insert_NotOverlappingAnyAppointment_Succeeds()
    {
        await using var context = TestContextFactory.CreateContext();
        AddFutureMondayAppointment(context); // 09:00-09:30 local, untouched
        var service = new ScheduleBlockService(context, TestContextFactory.CreateMapper());

        var startUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(13, 0));
        var request = new ScheduleBlockInsertRequest
        { DoctorId = 1, StartUtc = startUtc, EndUtc = startUtc.AddHours(1), Reason = "Sastanak" };

        var result = await service.InsertAsync(request);

        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task Update_ExtendedIntoAFutureAppointment_IsRejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var appointment = AddFutureMondayAppointment(context); // 09:00-09:30 local
        var service = new ScheduleBlockService(context, TestContextFactory.CreateMapper());

        var originalStartUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(13, 0));
        var block = await service.InsertAsync(new ScheduleBlockInsertRequest
        { DoctorId = 1, StartUtc = originalStartUtc, EndUtc = originalStartUtc.AddHours(1), Reason = "Sastanak" });

        // Extend the block backwards to now cover the 09:00 appointment.
        var extendedStartUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(8, 0));
        var request = new ScheduleBlockUpdateRequest
        { DoctorId = 1, StartUtc = extendedStartUtc, EndUtc = originalStartUtc.AddHours(1), Reason = "Sastanak" };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(block.Id, request));
        Assert.Contains($"#{appointment.Id}", ex.Message);
    }

    [Fact]
    public async Task Insert_OverlappingOnlyACancelledAppointment_Succeeds()
    {
        await using var context = TestContextFactory.CreateContext();
        var startUtc = ClinicTimeZone.ToUtc(NextMonday(), new TimeOnly(9, 0));
        context.Appointments.Add(new Appointment
        {
            PatientId = 1,
            DoctorId = 1,
            MedicalServiceId = 1,
            LocationId = 1,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(30),
            Status = AppointmentStatus.Cancelled,
            CancellationReason = "Test",
            CreatedByUserId = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new ScheduleBlockService(context, TestContextFactory.CreateMapper());

        var request = new ScheduleBlockInsertRequest { DoctorId = 1, StartUtc = startUtc, EndUtc = startUtc.AddHours(1), Reason = "Godišnji odmor" };

        var result = await service.InsertAsync(request);

        Assert.True(result.Id > 0);
    }
}
