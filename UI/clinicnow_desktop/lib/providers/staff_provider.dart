import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/staff_member.dart';

class StaffProvider extends BaseProvider<StaffMember> {
  StaffProvider(AuthSession authSession) : super('Staff', authSession);

  @override
  StaffMember fromJson(Map<String, dynamic> json) => StaffMember.fromJson(json);
}
