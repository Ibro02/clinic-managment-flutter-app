namespace ClinicNow.Model.Exceptions;

/// <summary>
/// Server-side input validation failed. Maps to HTTP 400 Bad Request together with
/// field-level error details, so the Flutter clients can render each message under
/// the relevant form control (rulebook §4 - messages must render below the control,
/// never as a generic dialog, and must state the exact format/constraint).
/// </summary>
public class ValidationException : ClinicNowException
{
    /// <summary>Field name -> list of error messages for that field.</summary>
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("Validacija unosa nije uspjela.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }
}
