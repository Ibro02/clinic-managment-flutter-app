/// Mirrors the backend's `LocationDto` - carries both `cityId` (the real FK,
/// used when editing) and `cityName` (denormalized purely for display; never
/// show raw IDs in the UI - rulebook Part II §K).
class Location {
  final int id;
  final String name;
  final String address;
  final int cityId;
  final String cityName;

  Location({
    required this.id,
    required this.name,
    required this.address,
    required this.cityId,
    required this.cityName,
  });

  factory Location.fromJson(Map<String, dynamic> json) => Location(
        id: json['id'] as int,
        name: json['name'] as String,
        address: json['address'] as String,
        cityId: json['cityId'] as int,
        cityName: json['cityName'] as String,
      );
}
