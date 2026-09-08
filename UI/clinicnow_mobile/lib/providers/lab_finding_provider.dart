import '../core/api_http.dart';

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/lab_finding.dart';

/// Mobile is read-only for lab findings (a patient views/downloads their own,
/// entering one is a doctor/lab-staff workflow, desktop-only) - same shape as
/// `MedicalDocumentProvider`.
class LabFindingProvider {
  final _LabFindingBaseProvider _base;

  LabFindingProvider(AuthSession authSession) : _base = _LabFindingBaseProvider(authSession);

  Future<List<LabFinding>> getPaged() async {
    final response = await apiGet(_base.buildUri('api/LabFinding', {'pageSize': 50}), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(LabFinding.fromJson).toList();
  }

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
