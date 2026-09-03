namespace ClinicNow.Model.Dto;

public class MedicalServiceDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The specialization a doctor must hold to perform this service.</summary>
    public int SpecializationId { get; set; }

    /// <summary>Denormalized for display - never show a raw ID (rulebook Part II §K).</summary>
    public string SpecializationName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }
}
