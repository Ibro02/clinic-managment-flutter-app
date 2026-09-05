/// Mirrors the backend's `ReferralDto`. Read-only on mobile - a referral is
/// created by a doctor during an examination (desktop-only), the patient
/// only ever views it and can continue into booking with the target
/// specialization (review item C5).
class Referral {
  final int id;
  final int referringDoctorId;
  final String referringDoctorName;
  final DateTime sourceAppointmentStartUtc;
  final int targetSpecializationId;
  final String targetSpecializationName;
  final String reason;

  /// True once an appointment has been booked "from" this referral - the
  /// server independently refuses a second booking against it either way,
  /// this just lets the UI hide "Zakaži termin" instead of offering a button
  /// that would fail.
  final bool isUsed;

  final DateTime createdAtUtc;

  const Referral({
    required this.id,
    required this.referringDoctorId,
    required this.referringDoctorName,
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
      referringDoctorId: json['referringDoctorId'] as int,
      referringDoctorName: json['referringDoctorName'] as String? ?? '',
      sourceAppointmentStartUtc: DateTime.parse(json['sourceAppointmentStartUtc'] as String),
      targetSpecializationId: json['targetSpecializationId'] as int,
      targetSpecializationName: json['targetSpecializationName'] as String? ?? '',
      reason: json['reason'] as String? ?? '',
      isUsed: json['isUsed'] as bool? ?? false,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
    );
  }
}
