import 'package:flutter/foundation.dart';

/// Holds the current authentication state for the whole app: the JWT access
/// token, its expiry, and the logged-in user's own profile/roles. A single
/// instance is created in `main.dart` and provided app-wide via
/// `ChangeNotifierProvider<AuthSession>` - screens/providers read it via
/// `context.read`/`context.watch`, and `main.dart` itself watches
/// [isLoggedIn] to decide which screen to show, so clearing the session from
/// anywhere (explicit logout, or [BaseProvider] reacting to an HTTP 401)
/// automatically redirects to the login screen (rulebook §5/Appendix A.2:
/// "istekle tokene ne treba ignorisati").
class AuthSession extends ChangeNotifier {
  String? _token;
  DateTime? _expiresAtUtc;
  int? _userId;
  String? _email;
  String? _firstName;
  String? _lastName;
  String? _phoneNumber;
  List<String> _roles = const [];

  String? get token => _token;
  DateTime? get expiresAtUtc => _expiresAtUtc;
  int? get userId => _userId;
  String? get email => _email;
  String? get firstName => _firstName;
  String? get lastName => _lastName;
  String? get phoneNumber => _phoneNumber;
  String get fullName => [_firstName, _lastName].where((s) => s != null && s.isNotEmpty).join(' ');
  List<String> get roles => List.unmodifiable(_roles);
  bool get isLoggedIn => _token != null;

  bool hasRole(String role) => _roles.contains(role);

  void setSession({
    required String token,
    required DateTime expiresAtUtc,
    required int userId,
    required String email,
    required String firstName,
    required String lastName,
    required List<String> roles,
  }) {
    _token = token;
    _expiresAtUtc = expiresAtUtc;
    _userId = userId;
    _email = email;
    _firstName = firstName;
    _lastName = lastName;
    _roles = roles;
    notifyListeners();
  }

  /// Applies a profile edit the server has already accepted. Separate from
  /// [setSession] because the token, expiry and roles are unchanged by a
  /// profile save - only the display fields move, and overwriting the token
  /// with a stale copy would be a real bug.
  void applyProfile({
    required String firstName,
    required String lastName,
    String? phoneNumber,
  }) {
    _firstName = firstName;
    _lastName = lastName;
    _phoneNumber = phoneNumber;
    notifyListeners();
  }

  /// Applies an email change the server has already accepted (review item 2:
  /// only an Administrator can reach this - see AuthApi.updateEmail). Separate
  /// from [applyProfile] since email isn't part of the ordinary profile edit
  /// every role uses; kept in the session so the dialog's subtitle and any
  /// other reader of [email] see the new address without a re-login.
  void updateEmail(String email) {
    _email = email;
    notifyListeners();
  }

  /// Clears the session - called on explicit logout, and automatically by
  /// [BaseProvider] whenever the API returns HTTP 401 (expired/invalid/
  /// revoked token).
  void clear() {
    _token = null;
    _expiresAtUtc = null;
    _userId = null;
    _email = null;
    _firstName = null;
    _lastName = null;
    _phoneNumber = null;
    _roles = const [];
    notifyListeners();
  }
}
