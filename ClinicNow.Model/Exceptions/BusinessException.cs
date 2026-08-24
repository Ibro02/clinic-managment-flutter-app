namespace ClinicNow.Model.Exceptions;

/// <summary>
/// A business rule was violated - e.g. double-booking a doctor, cancelling an
/// appointment past the 48h cutoff, confirming an already-completed appointment,
/// or paying for an already-paid item. Maps to HTTP 400 Bad Request.
///
/// The message must be specific enough to show directly to the end user
/// (rulebook §4: validation/error messages must state the exact constraint,
/// not a generic "Bad request").
/// </summary>
public class BusinessException : ClinicNowException
{
    public BusinessException(string message) : base(message)
    {
    }

    /// <summary>
    /// Wraps a lower-level exception (e.g. a DB FK-constraint <c>DbUpdateException</c>)
    /// so the full technical detail still reaches the server log via
    /// <c>ILogger&lt;T&gt;</c>, while <paramref name="message"/> - not
    /// <paramref name="innerException"/> - is what ever reaches the client.
    /// </summary>
    public BusinessException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
