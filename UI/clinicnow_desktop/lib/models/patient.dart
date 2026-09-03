import 'gender.dart';

/// Mirrors the backend's `PatientDto`.
class Patient {
  final int id;
  final int? userId;
  final String firstName;
  final String lastName;
  final String? personalIdNumber;
  final DateTime? dateOfBirth;
  final Gender? gender;
  final String? phoneNumber;
  final String? email;
  final String? address;
  final DateTime createdAtUtc;
  final int? medicalRecordId;

  /// Null unless this patient was fetched from the archived-patients view
  /// (`onlyDeleted: true`) - see `ArchivedPatientsScreen`.
  final DateTime? deletedAtUtc;

  Patient({
    required this.id,
    this.userId,
    required this.firstName,
    required this.lastName,
    this.personalIdNumber,
    this.dateOfBirth,
    this.gender,
    this.phoneNumber,
    this.email,
    this.address,
    required this.createdAtUtc,
    this.medicalRecordId,
    this.deletedAtUtc,
  });

  String get fullName => '$firstName $lastName';

  factory Patient.fromJson(Map<String, dynamic> json) => Patient(
        id: json['id'] as int,
        userId: json['userId'] as int?,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        personalIdNumber: json['personalIdNumber'] as String?,
        dateOfBirth: json['dateOfBirth'] == null ? null : DateTime.parse(json['dateOfBirth'] as String),
        gender: json['gender'] == null ? null : Gender.fromInt(json['gender'] as int),
        phoneNumber: json['phoneNumber'] as String?,
        email: json['email'] as String?,
        address: json['address'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
        medicalRecordId: json['medicalRecordId'] as int?,
        deletedAtUtc: json['deletedAtUtc'] == null ? null : DateTime.parse(json['deletedAtUtc'] as String),
      );
}
