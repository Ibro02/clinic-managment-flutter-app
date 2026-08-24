namespace ClinicNow.Model.Exceptions;

/// <summary>
/// The requested entity does not exist, or is soft-deleted and therefore invisible
/// to the caller. Maps to HTTP 404 Not Found.
/// </summary>
public class NotFoundException : ClinicNowException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"{entityName} sa ID-om '{key}' nije pronađen/a.")
    {
    }
}
