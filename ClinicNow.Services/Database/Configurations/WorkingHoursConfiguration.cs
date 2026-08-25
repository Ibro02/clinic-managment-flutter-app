using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.HasOne(w => w.Doctor)
            .WithMany(d => d.WorkingHoursList)
            .HasForeignKey(w => w.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Doctor 1: Mon-Fri 08:00-16:00. Doctor 2: Mon/Wed/Fri 09:00-15:00.
        builder.HasData(
            new WorkingHours { Id = 1, DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) },
            new WorkingHours { Id = 2, DoctorId = 1, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) },
            new WorkingHours { Id = 3, DoctorId = 1, DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) },
            new WorkingHours { Id = 4, DoctorId = 1, DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) },
            new WorkingHours { Id = 5, DoctorId = 1, DayOfWeek = DayOfWeek.Friday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) },
            new WorkingHours { Id = 6, DoctorId = 2, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(15, 0) },
            new WorkingHours { Id = 7, DoctorId = 2, DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(15, 0) },
            new WorkingHours { Id = 8, DoctorId = 2, DayOfWeek = DayOfWeek.Friday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(15, 0) });
    }
}
