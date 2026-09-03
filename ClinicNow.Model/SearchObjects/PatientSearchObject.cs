namespace ClinicNow.Model.SearchObjects;

public class PatientSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against first name + last name.</summary>
    public string? Name { get; set; }

    /// <summary>Partial match on the national ID number.</summary>
    public string? PersonalIdNumber { get; set; }

    /// <summary>
    /// When true, list archived (soft-deleted) patients instead of active ones -
    /// bypasses the global <c>ISoftDelete</c> query filter deliberately, for the
    /// dedicated "Arhivirani pacijenti" screen (Administrator/Staff only).
    /// </summary>
    public bool OnlyDeleted { get; set; }
}
