import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/medical_service.dart';

class MedicalServiceProvider extends BaseProvider<MedicalService> {
  MedicalServiceProvider(AuthSession authSession) : super('MedicalService', authSession);

  @override
  MedicalService fromJson(Map<String, dynamic> json) => MedicalService.fromJson(json);
}
