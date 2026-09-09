namespace ClinicNow.Model.SearchObjects;

public class DiagnosisSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on either the ICD-10 code or the name - a doctor searches by whichever they remember.</summary>
    public string? Search { get; set; }

    /// <summary>Narrows the list to diagnoses normally treated by one specialization.</summary>
    public int? SuggestedSpecializationId { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "Code", "Name" };
}
