namespace ClinicNow.Model.Requests;

public class SpecializationInsertRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public class SpecializationUpdateRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
