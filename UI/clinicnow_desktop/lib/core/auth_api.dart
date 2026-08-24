import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_exception.dart';
import 'base_provider.dart';

/// Thin client for `POST api/auth/login` (backend endpoint arrives in Phase
/// 1 - see PLAN.md). Kept separate from `BaseProvider<T>` since login
/// doesn't return a paged/CRUD entity.
class AuthApi {
  Future<LoginResult> login({
    required String username,
    required String password,
  }) async {
    final normalizedBase =
        BaseProvider.baseUrl.endsWith('/') ? BaseProvider.baseUrl : '${BaseProvider.baseUrl}/';
    final uri = Uri.parse('${normalizedBase}api/auth/login');

    // Credentials go in the POST body, never the query string (rulebook §5:
    // "Login kredencijali salju se u body-ju POST zahtjeva, nikada kroz
    // query string parametre.").
    final response = await http.post(
      uri,
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'username': username, 'password': password}),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      return LoginResult.fromJson(body);
    }

    var message = 'Prijava nije uspjela. Provjerite korisničko ime i lozinku.';
    try {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      final errors = body['errors'] as Map<String, dynamic>?;
      if (errors != null && errors.values.isNotEmpty) {
        final firstEntry = errors.values.first;
        if (firstEntry is List && firstEntry.isNotEmpty) {
          message = '${firstEntry.first}';
        }
      }
    } catch (_) {
      // Non-JSON or unexpected error body - keep the generic message above.
    }

    throw ApiException(statusCode: response.statusCode, message: message);
  }
}

class LoginResult {
  final String token;
  final String username;
  final List<String> roles;

  LoginResult({
    required this.token,
    required this.username,
    required this.roles,
  });

  factory LoginResult.fromJson(Map<String, dynamic> json) {
    return LoginResult(
      token: json['token'] as String,
      username: json['username'] as String,
      roles: (json['roles'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
    );
  }
}
