using ClinicNow.Model.Common;

namespace ClinicNow.Model.SearchObjects;

public class AppointmentSearchObject : BaseSearchObject
{
    public int? PatientId { get; set; }

    public int? DoctorId { get; set; }

    public AppointmentStatus? Status { get; set; }

    /// <summary>Only appointments starting at or after this instant (UTC).</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Only appointments starting at or before this instant (UTC).</summary>
    public DateTime? ToUtc { get; set; }
}
