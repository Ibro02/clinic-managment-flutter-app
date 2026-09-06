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

  // Value equality by id: the booking screen re-fetches this list whenever the
  // chosen doctor changes (review item C2), which produces brand-new instances
  // for services that were already selected. Without this override,
  // DropdownButtonFormField<MedicalService> compares by reference identity and
  // throws "there should be exactly one item with [DropdownButton]'s value" the
  // moment the list is refreshed out from under a stale selection.
  @override
  bool operator ==(Object other) => other is MedicalService && other.id == id;

  @override
  int get hashCode => id.hashCode;
}
