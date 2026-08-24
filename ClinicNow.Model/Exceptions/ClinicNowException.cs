namespace ClinicNow.Model.Exceptions;

/// <summary>
/// Base type for every exception that represents an <em>expected</em>, user-facing
/// failure (bad input, business rule violation, missing record) as opposed to an
/// unexpected server bug.
///
/// <c>ClinicNow.API.Filters.ExceptionFilter</c> catches this hierarchy and maps each
/// concrete type to a specific HTTP status with a clean, user-safe message - never a
/// stack trace or internal detail (rulebook Part II §D). Anything that is <em>not</em>
/// a <see cref="ClinicNowException"/> is treated as a genuine bug: logged in full via
/// <c>ILogger&lt;T&gt;</c> and returned to the client as a generic 500.
/// </summary>
public abstract class ClinicNowException : Exception
{
    protected ClinicNowException(string message) : base(message)
    {
    }

    protected ClinicNowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
