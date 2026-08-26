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
    final response = await http.get(_uri('api/Dashboard/summary'), headers: _headers);
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw ApiException(statusCode: response.statusCode, message: 'Greška prilikom učitavanja dashboarda.', fieldErrors: const {});
    }
    return DashboardSummary.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<List<int>> getAppointmentsReportPdf({
    required DateTime startDate,
    required DateTime endDate,
    int? doctorId,
    List<int>? statuses,
  }) async {
    final response = await http.get(
      _uri('api/Reports/appointments-pdf', {
        'startDate': _formatDate(startDate),
        'endDate': _formatDate(endDate),
        'doctorId': ?doctorId,
        if (statuses != null && statuses.isNotEmpty) 'statuses': statuses,
      }),
      headers: _headers,
    );
    return _pdfBytesOrThrow(response);
  }

  Future<List<int>> getRevenueReportPdf({required DateTime startDate, required DateTime endDate}) async {
    final response = await http.get(
      _uri('api/Reports/revenue-pdf', {'startDate': _formatDate(startDate), 'endDate': _formatDate(endDate)}),
      headers: _headers,
    );
    return _pdfBytesOrThrow(response);
  }

  List<int> _pdfBytesOrThrow(http.Response response) {
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw ApiException(statusCode: response.statusCode, message: 'Greška prilikom generisanja izvještaja.', fieldErrors: const {});
    }
    return response.bodyBytes;
  }

  void _handleAuth(http.Response response) {
    if (response.statusCode == 401) {
      _authSession.clear();
    }
  }

  String _formatDate(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
}
