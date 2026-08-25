/// Mirrors `ClinicNow.Model.Security.Roles` on the backend - role name
/// constants used for client-side UI gating (nav destinations, allowed-login
/// checks). The backend independently re-enforces every one of these on the
/// actual endpoints; this is purely about not showing/allowing something the
/// server would reject anyway.
abstract final class Roles {
  static const administrator = 'Administrator';
  static const staff = 'Staff';
  static const doctor = 'Doctor';
  static const patient = 'Patient';
}
