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
  bool _emailRemindersEnabled = true;
  String _preferredLanguage = 'bs';
  List<String> _roles = const [];

  String? get token => _token;
  DateTime? get expiresAtUtc => _expiresAtUtc;
  int? get userId => _userId;
  String? get email => _email;
  String? get firstName => _firstName;
  String? get lastName => _lastName;
  String get fullName => [_firstName, _lastName].where((s) => s != null && s.isNotEmpty).join(' ');

  /// Kept in the session so the shell's greeting and the profile screen never
  /// disagree after an edit (review item C7).
  String? get phoneNumber => _phoneNumber;

  bool get emailRemindersEnabled => _emailRemindersEnabled;

  /// "bs" or "en" - review item 7's "jezik aplikacije" from the prijava.
  /// Drives the language of notifications/emails this account receives
  /// (server-side, via `PatientMessages`); see the profile screen's dropdown.
  String get preferredLanguage => _preferredLanguage;
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
    String? phoneNumber,
    bool emailRemindersEnabled = true,
    String preferredLanguage = 'bs',
    required List<String> roles,
  }) {
    _token = token;
    _expiresAtUtc = expiresAtUtc;
    _userId = userId;
    _email = email;
    _firstName = firstName;
    _lastName = lastName;
    _phoneNumber = phoneNumber;
    _emailRemindersEnabled = emailRemindersEnabled;
    _preferredLanguage = preferredLanguage;
    _roles = roles;
    notifyListeners();
  }

  /// Applies a profile the server just confirmed, without touching the token
  /// (review item C7). A profile edit is not a new sign-in, so re-running
  /// [setSession] with a stale token would be the wrong shape - and dropping
  /// the token entirely would sign the user out for renaming themselves.
  void applyProfile({
    required String firstName,
    required String lastName,
    String? phoneNumber,
    required bool emailRemindersEnabled,
    required String preferredLanguage,
  }) {
    _firstName = firstName;
    _lastName = lastName;
    _phoneNumber = phoneNumber;
    _emailRemindersEnabled = emailRemindersEnabled;
    _preferredLanguage = preferredLanguage;
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
    _emailRemindersEnabled = true;
    _preferredLanguage = 'bs';
    _roles = const [];
    notifyListeners();
  }
}
