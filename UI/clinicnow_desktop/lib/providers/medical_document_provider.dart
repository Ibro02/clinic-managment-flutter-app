import '../core/api_http.dart';
import 'dart:convert';
import 'dart:typed_data';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/medical_document.dart';

/// No client-facing Update (a document is replaced by a new upload, never
/// edited in place) and Insert takes a Base64 file payload rather than the
/// generic `insert(Map)` shape, so this wraps [BaseProvider] rather than
/// extending its CRUD methods directly - same pattern as `NotificationProvider`.
class MedicalDocumentProvider {
  final _MedicalDocumentBaseProvider _base;

  MedicalDocumentProvider(AuthSession authSession) : _base = _MedicalDocumentBaseProvider(authSession);

  /// [fileName] is a case-insensitive partial match, filtered in SQL by
  /// `MedicalDocumentSearchObject.FileName` - not by the caller over an
  /// already-fetched page, which would only ever search the first 50 rows
  /// (rulebook Part II §D: filter at the DB).
  Future<List<MedicalDocument>> getPaged({int? patientId, String? fileName}) async {
    final query = <String, dynamic>{'pageSize': 50};
    if (patientId != null) query['patientId'] = patientId;
    final trimmedFileName = fileName?.trim();
    if (trimmedFileName != null && trimmedFileName.isNotEmpty) query['fileName'] = trimmedFileName;
    final response = await apiGet(_base.buildUri('api/MedicalDocument', query), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(MedicalDocument.fromJson).toList();
  }

  Future<MedicalDocument> upload({
    required int patientId,
    required String fileName,
    required String contentType,
    required Uint8List bytes,
    String? description,
  }) async {
    final response = await apiPost(
      _base.buildUri('api/MedicalDocument'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'patientId': patientId,
        'fileName': fileName,
        'contentType': contentType,
        'fileBase64': base64Encode(bytes),
        'description': description,
      }),
      timeout: BaseProvider.fileTransferTimeout,
    );
    return MedicalDocument.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  Future<void> delete(int id) async {
    final response = await apiDelete(_base.buildUri('api/MedicalDocument/$id'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }

  /// A full absolute URL clients can pass to `launchUrl`/download logic.
  String absoluteDownloadUrl(MedicalDocument document) {
    final normalizedBase = BaseProvider.baseUrl.endsWith('/')
        ? BaseProvider.baseUrl.substring(0, BaseProvider.baseUrl.length - 1)
        : BaseProvider.baseUrl;
    return '$normalizedBase${document.downloadUrl}';
  }

  Map<String, String> authHeaders() => _base.authHeaders();
}

class _MedicalDocumentBaseProvider extends BaseProvider<void> {
  _MedicalDocumentBaseProvider(AuthSession authSession) : super('MedicalDocument', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
