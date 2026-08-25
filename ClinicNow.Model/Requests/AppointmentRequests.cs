namespace ClinicNow.Model.Requests;

/// <summary>
/// Books a new appointment. <see cref="PatientId"/> is only honored when the
/// caller is Administrator/Staff (booking on behalf of a patient) - when a
/// Patient books for themselves, the server resolves their own Patient record
/// from the JWT and ignores this field entirely (rulebook §5: never trust the
/// client for whose data this is).
///
/// There is deliberately no <c>LocationId</c> here: a doctor practices at
/// exactly one clinic (<c>Doctor.LocationId</c>, 1:1), so the appointment's
/// location is always derived from the chosen doctor, never picked
/// independently - offering a free choice of clinic alongside the doctor
/// would let a caller select a combination that doesn't exist in reality.
/// </summary>
public class AppointmentInsertRequest
{
    public int? PatientId { get; set; }

    public int DoctorId { get; set; }

    public int MedicalServiceId { get; set; }

    public DateTime StartUtc { get; set; }
}

/// <summary>Cancellation always requires a reason (rulebook Part II §G).</summary>
public class AppointmentCancelRequest
{
    public string Reason { get; set; } = string.Empty;
}
