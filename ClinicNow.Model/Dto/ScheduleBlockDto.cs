namespace ClinicNow.Model.Dto;

public class ScheduleBlockDto
{
    public int Id { get; set; }

    public int DoctorId { get; set; }

    public string DoctorName { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Reason { get; set; } = string.Empty;
}
