/// Mirrors the backend's `SpecializationDto`.
class Specialization {
  final int id;
  final String name;
  final String? description;

  Specialization({required this.id, required this.name, this.description});

  factory Specialization.fromJson(Map<String, dynamic> json) => Specialization(
        id: json['id'] as int,
        name: json['name'] as String,
        description: json['description'] as String?,
      );
}
