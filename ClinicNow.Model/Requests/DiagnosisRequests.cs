namespace ClinicNow.Model.Requests;

public class DiagnosisInsertRequest
{
    /// <summary>ICD-10 code, e.g. "J06.9".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional - which specialization normally treats this diagnosis.</summary>
    public int? SuggestedSpecializationId { get; set; }
}

public class DiagnosisUpdateRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? SuggestedSpecializationId { get; set; }
}
