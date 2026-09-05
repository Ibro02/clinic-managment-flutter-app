import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/referral.dart';

/// Mobile is read-only for referrals (a doctor creates them during an
/// examination, desktop-only) - same shape as `LabFindingProvider`.
class ReferralProvider {
  final _ReferralBaseProvider _base;

  ReferralProvider(AuthSession authSession) : _base = _ReferralBaseProvider(authSession);

  Future<List<Referral>> getPaged({bool onlyArchived = false}) async {
    final response = await http.get(
      _base.buildUri('api/Referral', {'pageSize': 50, 'onlyArchived': onlyArchived}),
      headers: _base.authHeaders(),
    );
    final data = _base.decode(response) as Map<String, dynamic>;
    final list = (data['resultList'] as List).cast<Map<String, dynamic>>();
    return list.map(Referral.fromJson).toList();
  }
}

class _ReferralBaseProvider extends BaseProvider<void> {
  _ReferralBaseProvider(AuthSession authSession) : super('Referral', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
