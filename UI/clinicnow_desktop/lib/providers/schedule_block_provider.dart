import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/schedule_block.dart';

class ScheduleBlockProvider extends BaseProvider<ScheduleBlock> {
  ScheduleBlockProvider(AuthSession authSession) : super('ScheduleBlock', authSession);

  @override
  ScheduleBlock fromJson(Map<String, dynamic> json) => ScheduleBlock.fromJson(json);
}
