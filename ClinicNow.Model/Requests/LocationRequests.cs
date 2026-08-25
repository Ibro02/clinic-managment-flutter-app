namespace ClinicNow.Model.Requests;

public class LocationInsertRequest
{
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public int CityId { get; set; }
}

public class LocationUpdateRequest
{
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public int CityId { get; set; }
}
