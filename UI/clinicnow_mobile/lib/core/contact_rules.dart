/// Client-side mirror of `ClinicNow.Services/Validation/ContactRules.cs`
/// (review item C17).
///
/// The backend is the authority - it re-validates every one of these rules and
/// nothing here is trusted (rulebook §5). These exist so the user is told what
/// is wrong *under the field* as they type, instead of round-tripping to the
/// API for a message they could have had immediately (rulebook §4).
///
/// The patterns, limits and messages are deliberately identical to the C# ones.
/// If you change a rule, change it in both places - a client rule that is
/// stricter than the server's silently blocks legitimate input, and one that is
/// looser just moves the error to the submit button.
class ContactRules {
  ContactRules._();

  // Mirrors the EF column limits the values land in.
  static const int maxEmailLength = 256;
  static const int maxPhoneLength = 30;
  static const int maxNameLength = 100;
  static const int maxPersonalIdLength = 20;
  static const int maxAddressLength = 250;
  static const int maxLicenseNumberLength = 50;
  static const int maxBioLength = 1000;
  static const int minPasswordLength = 8;

  static const String emailMessage =
      'Unesite ispravnu email adresu (npr. ime@primjer.com).';
  static const String phoneMessage =
      'Unesite ispravan broj telefona (npr. +38761123456).';

  static final RegExp _emailPattern = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');
  static final RegExp _phonePattern = RegExp(r'^\+?[0-9 ]{6,20}$');

  /// Returns null when valid, or the message to render under the field.
  static String? email(String? value, {bool required = false}) {
    final trimmed = value?.trim() ?? '';
    if (trimmed.isEmpty) {
      return required ? emailMessage : null;
    }
    if (!_emailPattern.hasMatch(trimmed)) return emailMessage;
    if (trimmed.length > maxEmailLength) {
      return 'Email adresa može imati najviše $maxEmailLength karaktera.';
    }
    return null;
  }

  /// Phone is optional everywhere in this app, but a supplied one must be real.
  static String? phone(String? value) {
    final trimmed = value?.trim() ?? '';
    if (trimmed.isEmpty) return null;
    if (!_phonePattern.hasMatch(trimmed)) return phoneMessage;
    if (trimmed.length > maxPhoneLength) {
      return 'Broj telefona može imati najviše $maxPhoneLength karaktera.';
    }
    return null;
  }

  /// Free text held to its column length, and optionally to being present.
  static String? text(
    String? value, {
    required int maxLength,
    required String label,
    bool required = false,
  }) {
    final trimmed = value?.trim() ?? '';
    if (trimmed.isEmpty) {
      return required ? '$label je obavezno.' : null;
    }
    if (trimmed.length > maxLength) {
      return '$label može imati najviše $maxLength karaktera.';
    }
    return null;
  }
}
