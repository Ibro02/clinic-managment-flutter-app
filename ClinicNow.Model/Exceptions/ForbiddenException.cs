namespace ClinicNow.Model.Exceptions;

/// <summary>
/// The caller is authenticated but not allowed to perform this action on this
/// resource - e.g. a patient trying to read another patient's appointment or
/// medical document. Maps to HTTP 403 Forbidden.
///
/// Per rulebook Part II §F, ownership must always be enforced server-side: a
/// user may act on their own data, an Administrator may act on anyone's.
/// </summary>
public class ForbiddenException : ClinicNowException
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
