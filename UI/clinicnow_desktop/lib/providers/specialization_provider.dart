import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/specialization.dart';

class SpecializationProvider extends BaseProvider<Specialization> {
  SpecializationProvider(AuthSession authSession) : super('Specialization', authSession);

  @override
  Specialization fromJson(Map<String, dynamic> json) => Specialization.fromJson(json);
}
