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

  Future<http.Response> _get(Uri url, {Map<String, String>? headers}) =>
      BaseProvider.send(() => BaseProvider.client.get(url, headers: headers));

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

  /// The signed-in user's own profile. Read on opening the profile dialog
  /// rather than trusted from the session, because the login response never
  /// carried the phone number - and because it is the only way to learn the
  /// current `emailRemindersEnabled` value, which this app must preserve
  /// without showing (see [updateProfile]).
  Future<UserProfile> me(String token) async {
    final response = await _get(_uri('api/auth/me'), headers: {'Authorization': 'Bearer $token'});

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return UserProfile.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw _parseError(response, fallbackMessage: 'Profil nije učitan.');
  }

  /// Edits the signed-in user's own profile. No user id is sent: the server
  /// takes the identity from the JWT, which is what makes an endpoint that
  /// edits an account safe to expose to every role.
  ///
  /// [emailRemindersEnabled] is echoed back, not chosen here. The staff app has
  /// no control for it - the reminder it gates is only ever sent to a patient -
  /// but `UpdateProfileRequest.EmailRemindersEnabled` is a non-nullable bool, so
  /// omitting it would deserialize as `false` and silently switch the
  /// preference off. Pass through what [me] returned.
  ///
  /// [preferredLanguage] is likewise non-nullable on `UpdateProfileRequest` and
  /// defaults to "bs" server-side when omitted - so it must always be sent back,
  /// or every profile save (even one that doesn't touch language) would silently
  /// reset the account back to Bosnian.
  Future<UserProfile> updateProfile({
    required String token,
    required String firstName,
    required String lastName,
    String? phoneNumber,
    required bool emailRemindersEnabled,
    required String preferredLanguage,
  }) async {
    final response = await _put(
      _uri('api/auth/me'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({
        'firstName': firstName,
        'lastName': lastName,
        'phoneNumber': (phoneNumber != null && phoneNumber.trim().isNotEmpty) ? phoneNumber.trim() : null,
        'emailRemindersEnabled': emailRemindersEnabled,
        'preferredLanguage': preferredLanguage,
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

  /// Administrator-only: changes any account's login email by id, including
  /// the administrator's own - the one endpoint that makes email editable at
  /// all (review item: "Administrator treba imati SVE privilegije, pa čak da
  /// i sam sebi promjeni mail"). Rejected server-side for every other role.
  Future<UserProfile> updateEmail({
    required String token,
    required int userId,
    required String email,
  }) async {
    final response = await _put(
      _uri('api/auth/$userId/email'),
      headers: _authorizedJsonHeaders(token),
      body: jsonEncode({'email': email}),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return UserProfile.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw _parseError(response, fallbackMessage: 'Email adresa nije sačuvana.');
  }

  Map<String, String> _authorizedJsonHeaders(String token) => {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer $token',
      };

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
/// what `GET`/`PUT api/auth/me` return on their own.
class UserProfile {
  final int id;
  final String email;
  final String firstName;
  final String lastName;
  final String? phoneNumber;

  /// Not shown by this app - carried only so [AuthApi.updateProfile] can echo
  /// it back unchanged. See that method for why omitting it would be a bug.
  final bool emailRemindersEnabled;

  /// "bs" or "en" - drives the language of notifications/emails this account
  /// receives (server-side, via `PatientMessages`); see the "Jezik aplikacije"
  /// dropdown in [ProfileDialog].
  final String preferredLanguage;

  final List<String> roles;
  final bool isActive;

  const UserProfile({
    required this.id,
    required this.email,
    required this.firstName,
    required this.lastName,
    required this.phoneNumber,
    required this.emailRemindersEnabled,
    required this.preferredLanguage,
    required this.roles,
    required this.isActive,
  });

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        id: json['id'] as int,
        email: json['email'] as String,
        firstName: json['firstName'] as String,
        lastName: json['lastName'] as String,
        phoneNumber: json['phoneNumber'] as String?,
        // Absent on an API build predating the settings toggle: default to
        // "on", the server's own default, so echoing it back cannot turn a
        // preference off that nobody asked to change.
        emailRemindersEnabled: json['emailRemindersEnabled'] as bool? ?? true,
        // Same reasoning: an older API build has no such field yet, so fall
        // back to the clinic's own default rather than an empty/invalid value.
        preferredLanguage: json['preferredLanguage'] as String? ?? 'bs',
        roles: (json['roles'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
        isActive: json['isActive'] as bool? ?? true,
      );

  void applyTo(AuthSession session) => session.applyProfile(
        firstName: firstName,
        lastName: lastName,
        phoneNumber: phoneNumber,
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
