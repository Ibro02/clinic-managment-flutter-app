import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_exception.dart';
import 'auth_session.dart';
import 'base_provider.dart';

/// Thin client for the backend's `api/auth` endpoints. Kept separate from
/// `BaseProvider<T>` since none of these calls return a paged/CRUD entity.
class AuthApi {
  /// Every auth call goes through the one shared, connection-reusing client
  /// and is bounded by a timeout - see BaseProvider.send. Without the timeout a
  /// login on a dying mobile connection hangs forever on the spinner instead of
  /// surfacing an error the user can act on.
  Future<http.Response> _post(Uri url, {Map<String, String>? headers, Object? body}) =>
      BaseProvider.send(() => BaseProvider.client.post(url, headers: headers, body: body));


  Future<http.Response> _put(Uri url, {Map<String, String>? headers, Object? body}) =>
      BaseProvider.send(() => BaseProvider.client.put(url, headers: headers, body: body));

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
    final response = await _post(
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
    final response = await _post(
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
      await _post(
        _uri('api/auth/logout'),
        headers: {'Authorization': 'Bearer $token'},
      );
    } catch (_) {
      // Network failure during logout - local session still gets cleared.
    }
  }

  /// Edits the signed-in user's own profile (review item C7). No user id is
  /// sent: the server takes the identity from the JWT, which is what makes this
  /// endpoint safe to expose to the patient app at all.
  Future<UserProfile> updateProfile({
    required String token,
    required String firstName,
    required String lastName,
    String? phoneNumber,
    required bool emailRemindersEnabled,
  }) async {
    final response = await _put(
      _uri('api/auth/me'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({
        'firstName': firstName,
        'lastName': lastName,
        'phoneNumber': (phoneNumber != null && phoneNumber.trim().isNotEmpty) ? phoneNumber.trim() : null,
        'emailRemindersEnabled': emailRemindersEnabled,
      }),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return UserProfile.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw _parseError(response, fallbackMessage: 'Profil nije sačuvan.');
  }

  /// Changes the password and returns the replacement session.
  ///
  /// The server invalidates every token issued before the change - that is how a
  /// password change ends any other session someone else might be holding - so
  /// the token used to make this call stops working the moment it succeeds. The
  /// caller must store the [AuthResult] returned here, or the next request will
  /// come back 401 and bounce the user to the login screen.
  Future<AuthResult> changePassword({
    required String token,
    required String currentPassword,
    required String newPassword,
    required String confirmNewPassword,
  }) async {
    final response = await _post(
      _uri('api/auth/change-password'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({
        'currentPassword': currentPassword,
        'newPassword': newPassword,
        'confirmNewPassword': confirmNewPassword,
      }),
    );
    return _parseAuthResult(response, fallbackMessage: 'Lozinka nije promijenjena.');
  }

  /// Asks for a reset code by email. Succeeds whether or not the address has an
  /// account - the server deliberately does not say, so the app must not
  /// pretend to know either.
  Future<void> forgotPassword({required String email}) async {
    final response = await _post(
      _uri('api/auth/forgot-password'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'email': email}),
    );
    _expectSuccess(response, fallbackMessage: 'Zahtjev za resetovanje lozinke nije poslan.');
  }

  Future<void> resetPassword({
    required String email,
    required String code,
    required String newPassword,
    required String confirmNewPassword,
  }) async {
    final response = await _post(
      _uri('api/auth/reset-password'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'email': email,
        'code': code,
        'newPassword': newPassword,
        'confirmNewPassword': confirmNewPassword,
      }),
    );
    _expectSuccess(response, fallbackMessage: 'Lozinka nije resetovana.');
  }

  /// Subscribes this device to push notifications. No user id is sent - the
  /// backend takes the owner from the JWT, so a client cannot subscribe someone
  /// else's account to its own device.
  Future<void> registerDevice({
    required String token,
    required String deviceToken,
    required String platform,
  }) async {
    final response = await _post(
      _uri('api/DeviceToken/register'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({'token': deviceToken, 'platform': platform}),
    );
    _expectSuccess(response, fallbackMessage: 'Uređaj nije registrovan za obavijesti.');
  }

  /// Unsubscribes this device, on sign-out - so the next person to hold the
  /// phone does not receive the previous user's notifications.
  Future<void> unregisterDevice({
    required String token,
    required String deviceToken,
  }) async {
    final response = await _post(
      _uri('api/DeviceToken/unregister'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({'token': deviceToken}),
    );
    _expectSuccess(response, fallbackMessage: 'Uređaj nije odjavljen sa obavijesti.');
  }

  Map<String, String> _authorizedJsonHeaders(String token) => {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer $token',
      };

  /// For the endpoints that answer 204 with no body - there is nothing to
  /// decode, only a status to believe or a validation message to surface.
  void _expectSuccess(http.Response response, {required String fallbackMessage}) {
    if (response.statusCode >= 200 && response.statusCode < 300) return;
    throw _parseError(response, fallbackMessage: fallbackMessage);
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

/// Mirrors the backend's `UserDto` - the profile half of a login response, and
/// what `PUT api/auth/me` returns on its own.
class UserProfile {
  final int id;
  final String email;
  final String firstName;
  final String lastName;
  final String? phoneNumber;
  final bool emailRemindersEnabled;
  final List<String> roles;

  const UserProfile({
    required this.id,
    required this.email,
    required this.firstName,
    required this.lastName,
    required this.phoneNumber,
    required this.emailRemindersEnabled,
    required this.roles,
  });

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        id: json['id'] as int,
        email: json['email'] as String,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        phoneNumber: json['phoneNumber'] as String?,
        // Absent on an API build predating the settings toggle: default to
        // "on", which is the server's own default, rather than showing the
        // switch off and inviting the user to "fix" something that isn't wrong.
        emailRemindersEnabled: json['emailRemindersEnabled'] as bool? ?? true,
        roles: (json['roles'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
      );

  void applyTo(AuthSession session) => session.applyProfile(
        firstName: firstName,
        lastName: lastName,
        phoneNumber: phoneNumber,
        emailRemindersEnabled: emailRemindersEnabled,
      );
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
  final String? phoneNumber;
  final bool emailRemindersEnabled;
  final List<String> roles;

  AuthResult({
    required this.accessToken,
    required this.expiresAtUtc,
    required this.userId,
    required this.email,
    required this.firstName,
    required this.lastName,
    required this.phoneNumber,
    required this.emailRemindersEnabled,
    required this.roles,
  });

  factory AuthResult.fromJson(Map<String, dynamic> json) {
    final user = UserProfile.fromJson(json['user'] as Map<String, dynamic>);
    return AuthResult(
      accessToken: json['accessToken'] as String,
      expiresAtUtc: DateTime.parse(json['expiresAtUtc'] as String),
      userId: user.id,
      email: user.email,
      firstName: user.firstName,
      lastName: user.lastName,
      phoneNumber: user.phoneNumber,
      emailRemindersEnabled: user.emailRemindersEnabled,
      roles: user.roles,
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
      phoneNumber: phoneNumber,
      emailRemindersEnabled: emailRemindersEnabled,
      roles: roles,
    );
  }
}
