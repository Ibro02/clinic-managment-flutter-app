import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/appointment.dart';

class AppointmentProvider extends BaseProvider<Appointment> {
  AppointmentProvider(AuthSession authSession) : super('Appointment', authSession);

  @override
  Appointment fromJson(Map<String, dynamic> json) => Appointment.fromJson(json);

  Future<Appointment> confirm(int id) async {
    final response = await http.post(buildUri('api/Appointment/$id/confirm'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  Future<Appointment> complete(int id) async {
    final response = await http.post(buildUri('api/Appointment/$id/complete'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  Future<Appointment> cancel(int id, String reason) async {
    final response = await http.post(
      buildUri('api/Appointment/$id/cancel'),
      headers: authHeaders(),
      body: jsonEncode({'reason': reason}),
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
    final response = await http.get(
      buildUri('api/Appointment/available-slots', {'doctorId': doctorId, 'medicalServiceId': medicalServiceId, 'date': dateOnly}),
      headers: authHeaders(),
    );
    final list = decode(response) as List<dynamic>;
    return list.map((e) => DateTime.parse(e as String).toLocal()).toList();
  }
}
