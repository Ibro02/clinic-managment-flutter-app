namespace ClinicNow.Model.Requests;

public class ScheduleBlockInsertRequest
{
    public int DoctorId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public class ScheduleBlockUpdateRequest
{
    public int DoctorId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Reason { get; set; } = string.Empty;
}
