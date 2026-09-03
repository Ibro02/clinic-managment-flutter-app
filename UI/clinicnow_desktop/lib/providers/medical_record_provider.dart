import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/medical_record.dart';

/// The medical record is always addressed by `patientId` (there is exactly
/// one per patient), not a free-standing ID from the client - see the
/// backend's `IMedicalRecordService` for the same reasoning. Wraps
/// [BaseProvider] rather than extending its generic CRUD methods, same
/// pattern as `MedicalDocumentProvider`/`NotificationProvider`.
class MedicalRecordProvider {
  final _MedicalRecordBaseProvider _base;

  MedicalRecordProvider(AuthSession authSession) : _base = _MedicalRecordBaseProvider(authSession);

  Future<MedicalRecord> getByPatientId(int patientId) async {
    final response = await http.get(_base.buildUri('api/MedicalRecord/patient/$patientId'), headers: _base.authHeaders());
    return MedicalRecord.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Doctor-only: appends (never replaces) text onto Allergies/MedicalNotes.
  Future<MedicalRecord> appendNotes(int patientId, {String? allergiesToAppend, String? medicalNotesToAppend}) async {
    final response = await http.post(
      _base.buildUri('api/MedicalRecord/patient/$patientId/notes/append'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'allergiesToAppend': allergiesToAppend,
        'medicalNotesToAppend': medicalNotesToAppend,
      }),
    );
    return MedicalRecord.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Administrator-only: full replace of Allergies/MedicalNotes.
  Future<MedicalRecord> replaceNotes(int patientId, {String? allergies, String? medicalNotes}) async {
    final response = await http.put(
      _base.buildUri('api/MedicalRecord/patient/$patientId/notes'),
      headers: _base.authHeaders(),
      body: jsonEncode({'allergies': allergies, 'medicalNotes': medicalNotes}),
    );
    return MedicalRecord.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Doctor or Administrator: adds one row to the treatment history table.
  Future<MedicalRecord> addEntry(
    int patientId, {
    required DateTime entryDate,
    required String diagnosis,
    required String treatment,
    required String description,
  }) async {
    final response = await http.post(
      _base.buildUri('api/MedicalRecord/patient/$patientId/entries'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'entryDate': _dateOnly(entryDate),
        'diagnosis': diagnosis,
        'treatment': treatment,
        'description': description,
      }),
    );
    return MedicalRecord.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Administrator-only: edits an existing treatment-history row.
  Future<MedicalRecord> updateEntry(
    int entryId, {
    required DateTime entryDate,
    required String diagnosis,
    required String treatment,
    required String description,
  }) async {
    final response = await http.put(
      _base.buildUri('api/MedicalRecord/entries/$entryId'),
      headers: _base.authHeaders(),
      body: jsonEncode({
        'entryDate': _dateOnly(entryDate),
        'diagnosis': diagnosis,
        'treatment': treatment,
        'description': description,
      }),
    );
    return MedicalRecord.fromJson(_base.decode(response) as Map<String, dynamic>);
  }

  /// Administrator-only: removes a treatment-history row.
  Future<void> deleteEntry(int entryId) async {
    final response = await http.delete(_base.buildUri('api/MedicalRecord/entries/$entryId'), headers: _base.authHeaders());
    _base.decode(response, allowEmptyBody: true);
  }

  String _dateOnly(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
}

class _MedicalRecordBaseProvider extends BaseProvider<void> {
  _MedicalRecordBaseProvider(AuthSession authSession) : super('MedicalRecord', authSession);

  @override
  void fromJson(Map<String, dynamic> json) {}
}
