/// Mirrors the backend's `DoctorDto`.
class Doctor {
  final int id;
  final int userId;
  final String firstName;
  final String lastName;
  final String email;
  final String? phoneNumber;

  /// The one clinic this doctor practices at (1:1) - never chosen
  /// independently of the doctor when booking an appointment with them.
  final int locationId;
  final String locationName;

  final String? licenseNumber;
  final String? bio;
  final List<String> specializations;
  final List<int> specializationIds;

  Doctor({
    required this.id,
    required this.userId,
    required this.firstName,
    required this.lastName,
    required this.email,
    this.phoneNumber,
    required this.locationId,
    required this.locationName,
    this.licenseNumber,
    this.bio,
    required this.specializations,
    required this.specializationIds,
  });

  String get fullName => '$firstName $lastName';

  factory Doctor.fromJson(Map<String, dynamic> json) => Doctor(
        id: json['id'] as int,
        userId: json['userId'] as int,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        email: json['email'] as String,
        phoneNumber: json['phoneNumber'] as String?,
        locationId: json['locationId'] as int,
        locationName: json['locationName'] as String,
        licenseNumber: json['licenseNumber'] as String?,
        bio: json['bio'] as String?,
        specializations: (json['specializations'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
        specializationIds: (json['specializationIds'] as List<dynamic>? ?? []).map((e) => e as int).toList(),
      );
}
