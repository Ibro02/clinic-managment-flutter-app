namespace ClinicNow.Model.SearchObjects;

public class PatientSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against first name + last name.</summary>
    public string? Name { get; set; }

    /// <summary>Partial match on the national ID number.</summary>
    public string? PersonalIdNumber { get; set; }
}
