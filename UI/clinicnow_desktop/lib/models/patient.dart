/// Mirrors the backend's `PatientDto`.
class Patient {
  final int id;
  final int? userId;
  final String firstName;
  final String lastName;
  final String? personalIdNumber;
  final DateTime? dateOfBirth;
  final String? phoneNumber;
  final String? address;
  final DateTime createdAtUtc;

  Patient({
    required this.id,
    this.userId,
    required this.firstName,
    required this.lastName,
    this.personalIdNumber,
    this.dateOfBirth,
    this.phoneNumber,
    this.address,
    required this.createdAtUtc,
  });

  String get fullName => '$firstName $lastName';

  factory Patient.fromJson(Map<String, dynamic> json) => Patient(
        id: json['id'] as int,
        userId: json['userId'] as int?,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        personalIdNumber: json['personalIdNumber'] as String?,
        dateOfBirth: json['dateOfBirth'] == null ? null : DateTime.parse(json['dateOfBirth'] as String),
        phoneNumber: json['phoneNumber'] as String?,
        address: json['address'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      );
}
