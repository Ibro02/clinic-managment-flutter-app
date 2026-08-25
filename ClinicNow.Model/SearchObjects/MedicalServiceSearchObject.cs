namespace ClinicNow.Model.SearchObjects;

public class MedicalServiceSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on <c>Name</c>.</summary>
    public string? Name { get; set; }
}
