/// Mirrors the backend's `MedicalServiceDto`.
class MedicalService {
  final int id;
  final String name;
  final String? description;

  /// The specialization a doctor must hold to perform this service.
  final int specializationId;
  final String specializationName;
  final double price;
  final int durationMinutes;

  /// Booking this service requires an active referral targeting [specializationId].
  final bool isReferralRequired;

  MedicalService({
    required this.id,
    required this.name,
    this.description,
    required this.specializationId,
    required this.specializationName,
    required this.price,
    required this.durationMinutes,
    required this.isReferralRequired,
  });

  factory MedicalService.fromJson(Map<String, dynamic> json) => MedicalService(
        id: json['id'] as int,
        name: json['name'] as String,
        description: json['description'] as String?,
        specializationId: json['specializationId'] as int? ?? 0,
        specializationName: json['specializationName'] as String? ?? '',
        price: (json['price'] as num).toDouble(),
        durationMinutes: json['durationMinutes'] as int,
        isReferralRequired: json['isReferralRequired'] as bool? ?? false,
      );
}
