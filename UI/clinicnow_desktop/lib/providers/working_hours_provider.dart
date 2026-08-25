import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/working_hours.dart';

class WorkingHoursProvider extends BaseProvider<WorkingHours> {
  WorkingHoursProvider(AuthSession authSession) : super('WorkingHours', authSession);

  @override
  WorkingHours fromJson(Map<String, dynamic> json) => WorkingHours.fromJson(json);
}
