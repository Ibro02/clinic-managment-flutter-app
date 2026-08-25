namespace ClinicNow.Model.SearchObjects;

public class DoctorSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against first name + last name.</summary>
    public string? Name { get; set; }

    /// <summary>Only doctors with this specialization.</summary>
    public int? SpecializationId { get; set; }
}
