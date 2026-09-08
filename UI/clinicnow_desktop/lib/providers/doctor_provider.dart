import '../core/api_http.dart';

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/doctor.dart';

class DoctorProvider extends BaseProvider<Doctor> {
  DoctorProvider(AuthSession authSession) : super('Doctor', authSession);

  @override
  Doctor fromJson(Map<String, dynamic> json) => Doctor.fromJson(json);

  /// The signed-in doctor's own profile (`GET api/Doctor/me`). A doctor knows
  /// their user id but not their doctor id, so [getById] is no use here - and
  /// the server resolving it from the token is the point, not a convenience.
  Future<Doctor> getOwn() async {
    final response = await apiGet(buildUri('api/Doctor/me'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
