/// Mirrors the backend's `UserDto`, as returned by `api/Staff` - a Staff
/// account has no profile beyond the login itself, unlike Doctor/Patient.
class StaffMember {
  final int id;
  final String firstName;
  final String lastName;
  final String email;
  final String? phoneNumber;
  final bool isActive;

  StaffMember({
    required this.id,
    required this.firstName,
    required this.lastName,
    required this.email,
    this.phoneNumber,
    required this.isActive,
  });

  String get fullName => '$firstName $lastName';

  factory StaffMember.fromJson(Map<String, dynamic> json) => StaffMember(
        id: json['id'] as int,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        email: json['email'] as String,
        phoneNumber: json['phoneNumber'] as String?,
        isActive: json['isActive'] as bool? ?? true,
      );
}
