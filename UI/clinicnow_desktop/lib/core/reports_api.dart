import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/dashboard_summary.dart';
import 'api_exception.dart';
import 'auth_session.dart';
import 'base_provider.dart';

/// Thin client for `api/Dashboard` and `api/Reports` (Phase 9). Kept separate
/// from `BaseProvider<T>` (like `AuthApi`) since two of these calls return raw
/// PDF bytes and one returns a single aggregate object, never a paged list.
class ReportsApi {
  final AuthSession _authSession;

  ReportsApi(this._authSession);

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final normalizedBase =
        BaseProvider.baseUrl.endsWith('/') ? BaseProvider.baseUrl : '${BaseProvider.baseUrl}/';
    final uri = Uri.parse('$normalizedBase$path');
    if (query == null || query.isEmpty) return uri;

    final queryParameters = <String, dynamic>{};
    query.forEach((key, value) {
      if (value == null) return;
      queryParameters[key] = value is List ? value.map((e) => '$e').toList() : '$value';
    });
    return uri.replace(queryParameters: queryParameters);
  }

  Map<String, String> get _headers =>
      {if (_authSession.token != null) 'Authorization': 'Bearer ${_authSession.token}'};

  Future<DashboardSummary> getDashboardSummary() async {
    final response = await _get(_uri('api/Dashboard/summary'));
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw _parseError(response, fallbackMessage: 'Greška prilikom učitavanja dashboarda.');
    }
    return DashboardSummary.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<List<int>> getAppointmentsReportPdf({
    required DateTime startDate,
    required DateTime endDate,
    int? doctorId,
    List<int>? statuses,
  }) async {
    final response = await _get(
      _uri('api/Reports/appointments-pdf', {
        'startDate': _formatDate(startDate),
        'endDate': _formatDate(endDate),
        'doctorId': ?doctorId,
        if (statuses != null && statuses.isNotEmpty) 'statuses': statuses,
      }),
    );
    return _pdfBytesOrThrow(response);
  }

  Future<List<int>> getRevenueReportPdf({required DateTime startDate, required DateTime endDate}) async {
    final response = await _get(
      _uri('api/Reports/revenue-pdf', {'startDate': _formatDate(startDate), 'endDate': _formatDate(endDate)}),
    );
    return _pdfBytesOrThrow(response);
  }

  /// Wraps `http.get` so a network-layer failure (connection refused, DNS,
  /// timeout, ...) surfaces as an [ApiException] instead of an uncaught raw
  /// exception - there is no HTTP response to parse a message out of in that
  /// case, so a fixed "unreachable server" message is used (statusCode 0
  /// signals "no response received", matched by no real HTTP status).
  Future<http.Response> _get(Uri uri) async {
    try {
      return await http.get(uri, headers: _headers);
    } catch (_) {
      throw ApiException(
        statusCode: 0,
        message: 'Server nije dostupan. Provjerite internet konekciju i pokušajte ponovo.',
        fieldErrors: const {},
      );
    }
  }

  List<int> _pdfBytesOrThrow(http.Response response) {
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw _parseError(response, fallbackMessage: 'Greška prilikom generisanja izvještaja.');
    }
    return response.bodyBytes;
  }

  void _handleAuth(http.Response response) {
    if (response.statusCode == 401) {
      _authSession.clear();
    }
  }

  /// Surfaces the backend's specific validation message instead of a generic
  /// one (rulebook Appendix A.2), mirroring `AuthApi._parseError` /
  /// `BaseProvider._decode`: the error body is shaped
  /// `{ "errors": { "field": ["message"] } }`, and the first message found is
  /// used. Falls back to [fallbackMessage] when the body isn't JSON or has no
  /// `errors` key (e.g. a PDF response, or a non-JSON error page).
  ApiException _parseError(http.Response response, {required String fallbackMessage}) {
    var message = fallbackMessage;
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

  String _formatDate(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
}
