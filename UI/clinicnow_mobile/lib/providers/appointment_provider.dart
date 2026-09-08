import '../core/api_http.dart';
import 'dart:convert';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/appointment.dart';

class AppointmentProvider extends BaseProvider<Appointment> {
  AppointmentProvider(AuthSession authSession) : super('Appointment', authSession);

  @override
  Appointment fromJson(Map<String, dynamic> json) => Appointment.fromJson(json);

  Future<Appointment> confirm(int id) async {
    final response = await apiPost(buildUri('api/Appointment/$id/confirm'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  Future<Appointment> complete(int id) async {
    final response = await apiPost(buildUri('api/Appointment/$id/complete'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  Future<Appointment> cancel(int id, String reason) async {
    final response = await apiPost(
      buildUri('api/Appointment/$id/cancel'),
      headers: authHeaders(),
      body: jsonEncode({'reason': reason}),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  /// Moves the appointment to a new doctor/time (review item C6). The medical
  /// service can't change here - the server re-validates everything else
  /// (working hours, blocks, overlap, doctor↔service compatibility) exactly
  /// as it does for a new booking.
  Future<Appointment> reschedule(int id, {required int doctorId, required DateTime startUtc}) async {
    final response = await apiPost(
      buildUri('api/Appointment/$id/reschedule'),
      headers: authHeaders(),
      body: jsonEncode({'doctorId': doctorId, 'startUtc': startUtc.toUtc().toIso8601String()}),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  /// Real free start times (UTC) for a doctor+service on a given day - drives
  /// the booking flow's time picker (rulebook §7: only real free slots offered).
  Future<List<DateTime>> availableSlots({
    required int doctorId,
    required int medicalServiceId,
    required DateTime date,
  }) async {
    final dateOnly = '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
    final response = await apiGet(
      buildUri('api/Appointment/available-slots', {'doctorId': doctorId, 'medicalServiceId': medicalServiceId, 'date': dateOnly}),
      headers: authHeaders(),
    );
    final list = decode(response) as List<dynamic>;
    return list.map((e) => DateTime.parse(e as String).toLocal()).toList();
  }
}
