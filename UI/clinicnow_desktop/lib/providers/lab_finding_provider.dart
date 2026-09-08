import '../core/api_http.dart';
import 'dart:convert';
import 'dart:typed_data';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/lab_finding.dart';

/// No client-facing Update (a finding is entered once, never edited in place)
/// and Create takes a Base64 file payload rather than the generic
/// `insert(Map)` shape, so this wraps [BaseProvider] rather than extending its
/// CRUD methods directly - same pattern as `MedicalDocumentProvider`.
class LabFindingProvider {
  final _LabFindingBaseProvider _base;

  LabFindingProvider(AuthSession authSession) : _base = _LabFindingBaseProvider(authSession);

  Future<List<LabFinding>> getPaged({int? patientId, int? appointmentId}) async {
    final query = <String, dynamic>{'pageSize': 50};
    if (patientId != null) query['patientId'] = patientId;
    if (appointmentId != null) query['appointmentId'] = appointmentId;
    final response = await apiGet(_base.buildUri('api/LabFinding', query), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(LabFinding.fromJson).toList();
  }

  Future<LabFinding> create({
    required int appointmentId,
    required String result,
    required String fileName,
    required String contentType,
    required Uint8List bytes,
  }) async {
    final response = await apiPost(
      _base.buildUri('api/LabFinding'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'appointmentId': appointmentId,
        'result': result,
        'fileName': fileName,
        'contentType': contentType,
        'fileBase64': base64Encode(bytes),
      }),
      timeout: BaseProvider.fileTransferTimeout,
    );
    return LabFinding.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  Future<void> delete(int id) async {
    final response = await apiDelete(_base.buildUri('api/LabFinding/$id'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }

  /// A full absolute URL clients can pass to `launchUrl`/download logic.
  String absoluteDownloadUrl(LabFinding finding) {
    final normalizedBase = BaseProvider.baseUrl.endsWith('/')
        ? BaseProvider.baseUrl.substring(0, BaseProvider.baseUrl.length - 1)
        : BaseProvider.baseUrl;
    return '$normalizedBase${finding.downloadUrl}';
  }

  Map<String, String> authHeaders() => _base.authHeaders();
}

class _LabFindingBaseProvider extends BaseProvider<void> {
  _LabFindingBaseProvider(AuthSession authSession) : super('LabFinding', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
