namespace ClinicNow.Model.SearchObjects;

public class MedicalDocumentSearchObject : BaseSearchObject
{
    /// <summary>Scopes the list to one patient's documents (staff/doctor browsing a chart). Ignored/forced for the Patient role - see <c>MedicalDocumentService</c>.</summary>
    public int? PatientId { get; set; }

    /// <summary>Case-insensitive partial match against the file name.</summary>
    public string? FileName { get; set; }
}
