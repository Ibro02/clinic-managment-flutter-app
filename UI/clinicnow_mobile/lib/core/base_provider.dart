import 'dart:async';
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
///   read once as a `static const` (Part II §C);
/// - HTTP 401 responses clear the shared [AuthSession] instead of being
///   surfaced as a generic error (Appendix A.2).
///
/// Every request is bounded by a timeout and issued through one shared
/// [http.Client] - see [client] and [send].
abstract class BaseProvider<T> {
  /// Passed at build/run time, e.g.:
  ///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5203/   (Android emulator)
  ///   flutter run --dart-define=API_BASE_URL=http://localhost:5203/  (Windows desktop)
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5203/',
  );

  /// One client for the whole app rather than the `http.get`/`http.post`
  /// top-level helpers, each of which opens a connection and closes it again.
  /// Reusing it keeps connections alive between calls, removing a full TCP
  /// handshake per request - the cheapest latency win there is on a
  /// high-latency mobile network, and it matters most for the notification
  /// poller that fires every 20 seconds.
  static final http.Client _client = http.Client();

  /// The shared client, for callers issuing requests this class does not model
  /// (see `AuthApi`). Pair it with [send] so they inherit the same timeout and
  /// transport-error handling.
  static http.Client get client => _client;

  /// Ordinary API calls. Dart's default is effectively no timeout, so on a
  /// dying connection a request never completes and never fails - the user is
  /// left watching a spinner forever, with no error and no way out but killing
  /// the app. 20s is generous for a slow 3G round-trip and still bounded.
  static const Duration requestTimeout = Duration(seconds: 20);

  /// File transfers legitimately take longer than a JSON call: a 10 MB
  /// document over 3G will exceed [requestTimeout] without anything being
  /// wrong.
  static const Duration fileTransferTimeout = Duration(seconds: 120);

  final String _endpoint;
  final AuthSession _authSession;

  BaseProvider(String endpoint, AuthSession authSession)
      : _endpoint = endpoint,
        _authSession = authSession;

  /// Converts a decoded JSON map into a [T]. Implemented per entity, usually
  /// by delegating to a generated `T.fromJson` (json_serializable).
  T fromJson(Map<String, dynamic> json);

  /// Exposed (not `_`-prefixed) so subclasses with actions beyond plain CRUD
  /// (e.g. `AppointmentProvider.confirm`/`cancel`/`availableSlots`, hitting
  /// `POST api/Appointment/{id}/confirm` etc.) can build correctly-authorized
  /// requests without duplicating this logic.
  Uri buildUri(String path, [Map<String, dynamic>? query]) => _uri(path, query);

  Map<String, String> authHeaders() => _headers();

  /// Decodes a response the same way the CRUD methods below do (backend
  /// validation messages surfaced, session cleared on 401) - exposed for the
  /// same reason as [buildUri]/[authHeaders].
  dynamic decode(http.Response response, {bool allowEmptyBody = false}) =>
      _decode(response, allowEmptyBody: allowEmptyBody);

  /// Runs one HTTP call under a timeout and converts transport failures into
  /// an [ApiException] the UI already knows how to display.
  ///
  /// `statusCode: 0` marks "never reached the server". That is what lets a
  /// caller tell being offline apart from a genuine backend rejection - the
  /// distinction any offline fallback needs in order to be safe.
  static Future<http.Response> send(
    Future<http.Response> Function() request, {
    Duration? timeout,
  }) async {
    try {
      return await request().timeout(timeout ?? requestTimeout);
    } on TimeoutException {
      throw ApiException(
        statusCode: 0,
        message:
            'Server ne odgovara. Provjerite internet konekciju i pokušajte ponovo.',
      );
    } on http.ClientException {
      // Covers DNS failure, refused connection and a dropped socket, without
      // importing dart:io (which would not compile for a web target).
      throw ApiException(
        statusCode: 0,
        message:
            'Nije moguće povezati se sa serverom. Provjerite internet konekciju.',
      );
    }
  }

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
    final response = await send(
      () => _client.get(_uri('api/$_endpoint', search), headers: _headers()),
    );
    final data = _decode(response) as Map<String, dynamic>;
    return PagedResult<T>.fromJson(data, fromJson);
  }

  Future<T> getById(int id) async {
    final response = await send(
      () => _client.get(_uri('api/$_endpoint/$id'), headers: _headers()),
    );
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<T> insert(Map<String, dynamic> request) async {
    final response = await send(
      () => _client.post(
        _uri('api/$_endpoint'),
        headers: _headers(),
        body: jsonEncode(request),
      ),
      // An insert may carry a base64 file, so it gets the transfer budget.
      timeout: fileTransferTimeout,
    );
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<T> update(int id, Map<String, dynamic> request) async {
    final response = await send(
      () => _client.put(
        _uri('api/$_endpoint/$id'),
        headers: _headers(),
        body: jsonEncode(request),
      ),
      timeout: fileTransferTimeout,
    );
    return fromJson(_decode(response) as Map<String, dynamic>);
  }

  Future<void> delete(int id) async {
    final response = await send(
      () => _client.delete(_uri('api/$_endpoint/$id'), headers: _headers()),
    );
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
