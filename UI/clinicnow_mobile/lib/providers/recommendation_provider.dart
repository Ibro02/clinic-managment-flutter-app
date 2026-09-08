import '../core/api_http.dart';
import 'dart:convert';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/recommendation.dart';

class RecommendationProvider extends BaseProvider<AppointmentRecommendation> {
  RecommendationProvider(AuthSession authSession) : super('Recommendation', authSession);

  @override
  AppointmentRecommendation fromJson(Map<String, dynamic> json) => AppointmentRecommendation.fromJson(json);

  Future<List<AppointmentRecommendation>> getRecommendations() async {
    final response = await apiGet(buildUri('api/Recommendation/appointments'), headers: authHeaders());
    final list = decode(response) as List<dynamic>;
    return list.map((e) => fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Fire-and-forget from the UI's point of view (view/search logging must
  /// never block or fail the action the user actually cares about) - callers
  /// wrap this in a try/catch that swallows errors, same pattern as
  /// `AppShell._refreshUnreadCount`'s background poll.
  Future<void> logInteraction({required InteractionType type, int? doctorId, int? medicalServiceId}) async {
    final response = await apiPost(
      buildUri('api/Recommendation/interaction'),
      headers: authHeaders(),
      body: jsonEncode({
        'type': type.index,
        'doctorId': doctorId,
        'medicalServiceId': medicalServiceId,
      }),
    );
    decode(response, allowEmptyBody: true);
  }
}
