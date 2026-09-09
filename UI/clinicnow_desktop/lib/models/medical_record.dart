import 'gender.dart';

/// Mirrors the backend's `MedicalRecordEntryDto`.
class MedicalRecordEntry {
  final int id;
  final DateTime entryDate;

  /// The diagnosis is a reference into the diagnosis codebook, not free text -
  /// the raw [diagnosisId] never reaches the UI, [diagnosisDisplayName] does.
  final int diagnosisId;
  final String diagnosisCode;
  final String diagnosisName;

  /// Which specialization normally treats this diagnosis, when there is one -
  /// lets a referral be started straight from the entry.
  final int? diagnosisSpecializationId;

  /// Optional free-text qualifier on top of the coded diagnosis.
  final String? diagnosisNote;

  final String treatment;
  final String description;
  final String createdByName;
  final DateTime createdAtUtc;

  const MedicalRecordEntry({
    required this.id,
    required this.entryDate,
    required this.diagnosisId,
    required this.diagnosisCode,
    required this.diagnosisName,
    this.diagnosisSpecializationId,
    this.diagnosisNote,
    required this.treatment,
    required this.description,
    required this.createdByName,
    required this.createdAtUtc,
  });

  /// "J06.9 - Akutna infekcija gornjih disajnih puteva".
  String get diagnosisDisplayName => '$diagnosisCode - $diagnosisName';

  factory MedicalRecordEntry.fromJson(Map<String, dynamic> json) => MedicalRecordEntry(
        id: json['id'] as int,
        entryDate: DateTime.parse(json['entryDate'] as String),
        diagnosisId: json['diagnosisId'] as int? ?? 0,
        diagnosisCode: json['diagnosisCode'] as String? ?? '',
        diagnosisName: json['diagnosisName'] as String? ?? '',
        diagnosisSpecializationId: json['diagnosisSpecializationId'] as int?,
        diagnosisNote: json['diagnosisNote'] as String?,
        treatment: json['treatment'] as String? ?? '',
        description: json['description'] as String? ?? '',
        createdByName: json['createdByName'] as String? ?? '',
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      );
}

/// Mirrors the backend's `MedicalRecordDto` - the full medical file
/// ("medicinski karton"): header (denormalized patient basics), allergies/
/// notes, and the treatment history table.
class MedicalRecord {
  final int id;
  final int patientId;
  final String patientFirstName;
  final String patientLastName;
  final int? patientAge;
  final Gender? patientGender;
  final String? patientAddress;
  final String? patientEmail;
  final String? patientPhoneNumber;
  final String? allergies;
  final String? medicalNotes;
  final DateTime createdAtUtc;
  final DateTime updatedAtUtc;
  final List<MedicalRecordEntry> entries;

  const MedicalRecord({
    required this.id,
    required this.patientId,
    required this.patientFirstName,
    required this.patientLastName,
    this.patientAge,
    this.patientGender,
    this.patientAddress,
    this.patientEmail,
    this.patientPhoneNumber,
    this.allergies,
    this.medicalNotes,
    required this.createdAtUtc,
    required this.updatedAtUtc,
    required this.entries,
  });

  factory MedicalRecord.fromJson(Map<String, dynamic> json) => MedicalRecord(
        id: json['id'] as int,
        patientId: json['patientId'] as int,
        patientFirstName: json['patientFirstName'] as String? ?? '',
        patientLastName: json['patientLastName'] as String? ?? '',
        patientAge: json['patientAge'] as int?,
        patientGender: json['patientGender'] == null ? null : Gender.fromInt(json['patientGender'] as int),
        patientAddress: json['patientAddress'] as String?,
        patientEmail: json['patientEmail'] as String?,
        patientPhoneNumber: json['patientPhoneNumber'] as String?,
        allergies: json['allergies'] as String?,
        medicalNotes: json['medicalNotes'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
        updatedAtUtc: DateTime.parse(json['updatedAtUtc'] as String),
        entries: (json['entries'] as List).cast<Map<String, dynamic>>().map(MedicalRecordEntry.fromJson).toList(),
      );
}
