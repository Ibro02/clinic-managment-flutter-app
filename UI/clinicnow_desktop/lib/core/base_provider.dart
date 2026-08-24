import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/paged_result.dart';
import 'api_exception.dart';
import 'auth_session.dart';

/// Generic REST client for a single API resource - one concrete subclass per
/// entity (e.g. `PatientProvider extends BaseProvider<Patient>`), mirroring
/// the reference repo's `BaseProvider<T>` pattern with two deliberate
/// modernizations required by the 2025/26 rulebook:
///
/// - the API base URL comes from `String.fromEnvironment('API_BASE_URL')`,
///   read once as a `static const` (Part II §C), not from `flutter_dotenv`;
/// - HTTP 401 responses clear the shared [AuthSession] instead of being
///   surfaced as a generic error (Appendix A.2).
abstract class BaseProvider<T> {
  /// Passed at build/run time, e.g.:
  ///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5203/   (Android emulator)
  ///   flutter run --dart-define=API_BASE_URL=http://localhost:5203/  (Windows desktop)
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5203/',
  );

  final String _endpoint;
  final AuthSession _authSession;

  BaseProvider(String endpoint, AuthSession authSession)
      : _endpoint = endpoint,
        _authSession = authSession;

  /// Converts a decoded JSON map into a [T]. Implemented per entity, usually
  /// by delegating to a generated `T.fromJson` (json_serializable).
  T fromJson(Map<String, dynamic> json);

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final normalizedBase = baseUrl.endsWith('/') ? baseUrl : '$baseUrl/';
    final uri = Uri.parse('$normalizedBase$path');
    if (query == null || query.isEmpty) return uri;
    final stringQuery = <String, String>{};
    query.forEach((key, value) {
      if (value != null) stringQuery[key] = '$value';
    });
    return uri.replace(queryParameters: stringQuery);
  }

  Map<String, String> _headers() => {
        'Content-Type': 'application/json',
        if (_authSession.token != null)
          'Authorization': 'Bearer ${_authSession.token}',
      };

  Future<PagedResult<T>> getPaged([Map<String, dynamic>? search]) async {
    final response =
        await http.get(_uri('api/$_endpoint', search), headers: _headers());
    final data = _decode(response) as Map<String, dynamic>;
    return PagedResult<T>.fromJson(data, fromJson);
  }

  Future<T> getById(int id) async {
    final response =
        await http.get(_uri('api/$_endpoint/$id'), headers: _headers());
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<T> insert(Map<String, dynamic> request) async {
    final response = await http.post(
      _uri('api/$_endpoint'),
      headers: _headers(),
      body: jsonEncode(request),
    );
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<T> update(int id, Map<String, dynamic> request) async {
    final response = await http.put(
      _uri('api/$_endpoint/$id'),
      headers: _headers(),
      body: jsonEncode(request),
    );
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<void> delete(int id) async {
    final response =
        await http.delete(_uri('api/$_endpoint/$id'), headers: _headers());
    _decode(response, allowEmptyBody: true);
  }

  /// Parses the response, surfacing backend validation messages instead of a
  /// generic error (rulebook Appendix A.2), and clearing the session on 401
  /// so the UI can redirect to login rather than silently ignoring an
  /// expired/invalid token (rulebook Appendix A.2).
  dynamic _decode(http.Response response, {bool allowEmptyBody = false}) {
    final isSuccess = response.statusCode >= 200 && response.statusCode < 300;

    if (isSuccess) {
      if (response.body.isEmpty) return allowEmptyBody ? null : <String, dynamic>{};
      return jsonDecode(response.body);
    }

    if (response.statusCode == 401) {
      _authSession.clear();
    }

    final fieldErrors = <String, List<String>>{};
    var message =
        'Greška prilikom komunikacije sa serverom (${response.statusCode}).';

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
      // Non-JSON or unexpected error body - fall back to the generic message
      // above instead of throwing a secondary parsing exception.
    }

    throw ApiException(
      statusCode: response.statusCode,
      message: message,
      fieldErrors: fieldErrors,
    );
  }
}
