import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/patient.dart';

class PatientProvider extends BaseProvider<Patient> {
  PatientProvider(AuthSession authSession) : super('Patient', authSession);

  @override
  Patient fromJson(Map<String, dynamic> json) => Patient.fromJson(json);

  /// Un-archives a soft-deleted patient. See `ArchivedPatientsScreen`.
  Future<Patient> restore(int id) async {
    final response = await http.post(buildUri('api/Patient/$id/restore'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
