import 'package:flutter/foundation.dart';

/// Holds the current authentication state for the whole app: the JWT access
/// token and basic identity claims. A single instance is created in
/// `main.dart` and provided app-wide via `ChangeNotifierProvider<AuthSession>`
/// - screens/providers read it via `context.read`/`context.watch` instead of
/// the reference repo's raw static-field bag, so it's testable and reactive.
///
/// Real login/register wiring lands in Phase 1 once the backend Auth
/// endpoints exist; this class is already the final shape, so screens built
/// now (see LoginScreen) don't need structural changes later.
class AuthSession extends ChangeNotifier {
  String? _token;
  String? _username;
  List<String> _roles = const [];

  String? get token => _token;
  String? get username => _username;
  List<String> get roles => List.unmodifiable(_roles);
  bool get isLoggedIn => _token != null;

  void setSession({
    required String token,
    required String username,
    required List<String> roles,
  }) {
    _token = token;
    _username = username;
    _roles = roles;
    notifyListeners();
  }

  /// Clears the session - called on explicit logout, and automatically by
  /// [BaseProvider] whenever the API returns HTTP 401 (expired/invalid
  /// token), per rulebook Appendix A.2 ("Frontend mora pravilno obraditi
  /// HTTP 401 odgovor (redirect na login ili refresh token mehanizam)").
  void clear() {
    _token = null;
    _username = null;
    _roles = const [];
    notifyListeners();
  }
}
