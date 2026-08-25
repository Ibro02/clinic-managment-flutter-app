/// Mirrors the backend's `MedicalServiceDto`.
class MedicalService {
  final int id;
  final String name;
  final String? description;
  final double price;
  final int durationMinutes;

  MedicalService({
    required this.id,
    required this.name,
    this.description,
    required this.price,
    required this.durationMinutes,
  });

  factory MedicalService.fromJson(Map<String, dynamic> json) => MedicalService(
        id: json['id'] as int,
        name: json['name'] as String,
        description: json['description'] as String?,
        price: (json['price'] as num).toDouble(),
        durationMinutes: json['durationMinutes'] as int,
      );
}
