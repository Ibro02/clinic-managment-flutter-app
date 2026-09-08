import '../core/api_http.dart';
import 'dart:convert';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/referral.dart';

/// No client-facing Update/Delete (a referral stays part of the medical
/// history once created), so this wraps [BaseProvider] rather than extending
/// its generic CRUD methods directly - same pattern as `LabFindingProvider`.
class ReferralProvider {
  final _ReferralBaseProvider _base;

  ReferralProvider(AuthSession authSession) : _base = _ReferralBaseProvider(authSession);

  Future<List<Referral>> getPaged({int? patientId, bool onlyArchived = false, String? search}) async {
    final query = <String, dynamic>{'pageSize': 50, 'onlyArchived': onlyArchived};
    if (patientId != null) query['patientId'] = patientId;
    if (search != null && search.isNotEmpty) query['search'] = search;
    final response = await apiGet(_base.buildUri('api/Referral', query), headers: _base.authHeaders());
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(Referral.fromJson).toList();
  }

  Future<Referral> create({
    required int sourceAppointmentId,
    required int targetSpecializationId,
    required String reason,
  }) async {
    final response = await apiPost(
      _base.buildUri('api/Referral'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'sourceAppointmentId': sourceAppointmentId,
        'targetSpecializationId': targetSpecializationId,
        'reason': reason,
      }),
    );
    return Referral.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Administrator-only override to remove a mistaken referral (soft-delete
  /// server-side, never a physical removal).
  Future<void> delete(int id) async {
    final response = await apiDelete(_base.buildUri('api/Referral/$id'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }
}

class _ReferralBaseProvider extends BaseProvider<void> {
  _ReferralBaseProvider(AuthSession authSession) : super('Referral', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
