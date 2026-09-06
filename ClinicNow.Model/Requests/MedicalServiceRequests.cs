namespace ClinicNow.Model.Requests;

public class MedicalServiceInsertRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Required: the specialization a doctor must hold to perform this service.</summary>
    public int SpecializationId { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Booking this service will require an active referral targeting <see cref="SpecializationId"/>.</summary>
    public bool IsReferralRequired { get; set; }
}

public class MedicalServiceUpdateRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Required: the specialization a doctor must hold to perform this service.</summary>
    public int SpecializationId { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Booking this service will require an active referral targeting <see cref="SpecializationId"/>.</summary>
    public bool IsReferralRequired { get; set; }
}
