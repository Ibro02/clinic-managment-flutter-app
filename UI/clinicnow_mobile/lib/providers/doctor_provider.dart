import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/doctor.dart';

class DoctorProvider extends BaseProvider<Doctor> {
  DoctorProvider(AuthSession authSession) : super('Doctor', authSession);

  @override
  Doctor fromJson(Map<String, dynamic> json) => Doctor.fromJson(json);
}
