import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/patient.dart';

class PatientProvider extends BaseProvider<Patient> {
  PatientProvider(AuthSession authSession) : super('Patient', authSession);

  @override
  Patient fromJson(Map<String, dynamic> json) => Patient.fromJson(json);
}
