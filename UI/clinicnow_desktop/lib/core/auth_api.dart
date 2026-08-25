import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_exception.dart';
import 'auth_session.dart';
import 'base_provider.dart';

/// Thin client for the backend's `api/auth` endpoints. Kept separate from
/// `BaseProvider<T>` since none of these calls return a paged/CRUD entity.
class AuthApi {
  Uri _uri(String path) {
    final normalizedBase =
        BaseProvider.baseUrl.endsWith('/') ? BaseProvider.baseUrl : '${BaseProvider.baseUrl}/';
    return Uri.parse('$normalizedBase$path');
  }

  Future<AuthResult> login({
    required String email,
    required String password,
  }) async {
    // Credentials go in the POST body, never the query string (rulebook §5).
    final response = await http.post(
      _uri('api/auth/login'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'email': email, 'password': password}),
    );
    return _parseAuthResult(response, fallbackMessage: 'Prijava nije uspjela. Provjerite email i lozinku.');
  }

  Future<AuthResult> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    String? phoneNumber,
  }) async {
    final response = await http.post(
      _uri('api/auth/register'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'email': email,
        'password': password,
        'firstName': firstName,
        'lastName': lastName,
        if (phoneNumber != null && phoneNumber.isNotEmpty) 'phoneNumber': phoneNumber,
      }),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      // Registration only returns the created profile, not a token - the user
      // still has to log in explicitly afterwards (matches most real systems
      // and keeps "register" and "login" as two clearly separate actions).
      return login(email: email, password: password);
    }

    throw _parseError(response);
  }

  /// Best-effort server-side logout (rulebook §5: "Logout mora nevalidirati
  /// token na serverskoj strani, ne samo lokalno"). Failures are swallowed -
  /// the local session is always cleared by the caller regardless, since an
  /// unreachable server must never trap the user in a logged-in UI state.
  Future<void> logout(String token) async {
    try {
      await http.post(
        _uri('api/auth/logout'),
        headers: {'Authorization': 'Bearer $token'},
      );
    } catch (_) {
      // Network failure during logout - local session still gets cleared.
    }
  }

  AuthResult _parseAuthResult(http.Response response, {required String fallbackMessage}) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      return AuthResult.fromJson(body);
    }
    throw _parseError(response, fallbackMessage: fallbackMessage);
  }

  ApiException _parseError(http.Response response, {String? fallbackMessage}) {
    var message = fallbackMessage ??
        'Greška prilikom komunikacije sa serverom (${response.statusCode}).';
    final fieldErrors = <String, List<String>>{};

    try {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      final errors = body['errors'] as Map<String, dynamic>?;
      if (errors != null) {
        errors.forEach((key, value) {
          fieldErrors[key] = (value as List).map((e) => '$e').toList();
        });
        if (fieldErrors.values.isNotEmpty) {
          final firstList = fieldErrors.values.first;
          if (firstList.isNotEmpty) message = firstList.first;
        }
      }
    } catch (_) {
      // Non-JSON or unexpected error body - keep the fallback message above.
    }

    return ApiException(statusCode: response.statusCode, message: message, fieldErrors: fieldErrors);
  }
}

/// Result of a successful login/register - mirrors the backend's
/// `LoginResponseDto`.
class AuthResult {
  final String accessToken;
  final DateTime expiresAtUtc;
  final int userId;
  final String email;
  final String firstName;
  final String lastName;
  final List<String> roles;

  AuthResult({
    required this.accessToken,
    required this.expiresAtUtc,
    required this.userId,
    required this.email,
    required this.firstName,
    required this.lastName,
    required this.roles,
  });

  factory AuthResult.fromJson(Map<String, dynamic> json) {
    final user = json['user'] as Map<String, dynamic>;
    return AuthResult(
      accessToken: json['accessToken'] as String,
      expiresAtUtc: DateTime.parse(json['expiresAtUtc'] as String),
      userId: user['id'] as int,
      email: user['email'] as String,
      firstName: user['firstName'] as String,
      lastName: user['lastName'] as String,
      roles: (user['roles'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
    );
  }

  void applyTo(AuthSession session) {
    session.setSession(
      token: accessToken,
      expiresAtUtc: expiresAtUtc,
      userId: userId,
      email: email,
      firstName: firstName,
      lastName: lastName,
      roles: roles,
    );
  }
}
