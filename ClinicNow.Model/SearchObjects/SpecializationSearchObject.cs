namespace ClinicNow.Model.SearchObjects;

public class SpecializationSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on <c>Name</c>.</summary>
    public string? Name { get; set; }
}
