/// Mirrors the backend's `ReferralDto`. No client-facing Update/Delete - a
/// referral stays part of the medical history once created.
class Referral {
  final int id;
  final int patientId;
  final String patientName;
  final int referringDoctorId;
  final String referringDoctorName;
  final int sourceAppointmentId;
  final DateTime sourceAppointmentStartUtc;
  final int targetSpecializationId;
  final String targetSpecializationName;
  final String reason;

  /// True once an appointment has been booked "from" this referral (mobile
  /// only, review item C5) - shown here mainly so staff can tell at a glance
  /// which active referrals are still awaiting the patient to act on them.
  final bool isUsed;

  final DateTime createdAtUtc;

  const Referral({
    required this.id,
    required this.patientId,
    required this.patientName,
    required this.referringDoctorId,
    required this.referringDoctorName,
    required this.sourceAppointmentId,
    required this.sourceAppointmentStartUtc,
    required this.targetSpecializationId,
    required this.targetSpecializationName,
    required this.reason,
    required this.isUsed,
    required this.createdAtUtc,
  });

  factory Referral.fromJson(Map<String, dynamic> json) {
    return Referral(
      id: json['id'] as int,
      patientId: json['patientId'] as int,
      patientName: json['patientName'] as String? ?? '',
      referringDoctorId: json['referringDoctorId'] as int,
      referringDoctorName: json['referringDoctorName'] as String? ?? '',
      sourceAppointmentId: json['sourceAppointmentId'] as int,
      sourceAppointmentStartUtc: DateTime.parse(json['sourceAppointmentStartUtc'] as String),
      targetSpecializationId: json['targetSpecializationId'] as int,
      targetSpecializationName: json['targetSpecializationName'] as String? ?? '',
      reason: json['reason'] as String? ?? '',
      isUsed: json['isUsed'] as bool? ?? false,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
    );
  }
}
