import '../core/api_http.dart';

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/medical_document.dart';

/// Mobile is read-only for documents (patients view/download their own,
/// they never upload - that's a clinic-staff workflow, desktop-only), so this
/// only exposes list + a download-URL helper, wrapping [BaseProvider] the
/// same way `NotificationProvider` does.
class MedicalDocumentProvider {
  final _MedicalDocumentBaseProvider _base;

  MedicalDocumentProvider(AuthSession authSession) : _base = _MedicalDocumentBaseProvider(authSession);

  Future<List<MedicalDocument>> getPaged() async {
    final response = await apiGet(_base.buildUri('api/MedicalDocument', {'pageSize': 50}), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(MedicalDocument.fromJson).toList();
  }

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
