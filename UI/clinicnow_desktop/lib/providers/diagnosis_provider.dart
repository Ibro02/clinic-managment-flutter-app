import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/diagnosis.dart';

class DiagnosisProvider extends BaseProvider<Diagnosis> {
  DiagnosisProvider(AuthSession authSession) : super('Diagnosis', authSession);

  @override
  Diagnosis fromJson(Map<String, dynamic> json) => Diagnosis.fromJson(json);

  /// The whole codebook, for the medical-record entry dropdown. Bounded by the
  /// backend's `MaxPageSize` of 100 rather than fetched unpaged - the codebook
  /// is small by design, and an unbounded read is a defect either way
  /// (rulebook §8.2).
  Future<List<Diagnosis>> getAllForDropdown() async {
    final result = await getPaged({'page': 1, 'pageSize': 100, 'orderBy': 'Code'});
    return result.resultList;
  }
}
