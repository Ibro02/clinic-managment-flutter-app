namespace ClinicNow.Model.Dto;

public class MedicalServiceDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }
}
