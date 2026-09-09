/// Mirrors the backend's `LabFindingDto`. Never carries raw file bytes - only
/// [downloadUrl] (the actual bytes are served from a dedicated endpoint).
class LabFinding {
  final int id;
  final int patientId;
  final String patientName;
  final int appointmentId;
  final DateTime appointmentStartUtc;
  final String medicalServiceName;
  final String testName;
  final String? value;
  final String? unit;
  final String? referenceRange;
  final String result;
  final String? doctorNote;
  final String? fileName;
  final String? contentType;
  final int fileSizeBytes;
  final String enteredByName;
  final DateTime createdAtUtc;
  final String downloadUrl;

  /// Whether a document is attached at all - the attachment is optional, so
  /// the "Preuzmi" action is only offered when this is true.
  final bool hasFile;

  const LabFinding({
    required this.id,
    required this.patientId,
    required this.patientName,
    required this.appointmentId,
    required this.appointmentStartUtc,
    required this.medicalServiceName,
    required this.testName,
    this.value,
    this.unit,
    this.referenceRange,
    required this.result,
    this.doctorNote,
    this.fileName,
    this.contentType,
    required this.fileSizeBytes,
    required this.enteredByName,
    required this.createdAtUtc,
    required this.downloadUrl,
    required this.hasFile,
  });

  factory LabFinding.fromJson(Map<String, dynamic> json) {
    return LabFinding(
      id: json['id'] as int,
      patientId: json['patientId'] as int,
      patientName: json['patientName'] as String? ?? '',
      appointmentId: json['appointmentId'] as int,
      appointmentStartUtc: DateTime.parse(json['appointmentStartUtc'] as String),
      medicalServiceName: json['medicalServiceName'] as String? ?? '',
      testName: json['testName'] as String? ?? '',
      value: json['value'] as String?,
      unit: json['unit'] as String?,
      referenceRange: json['referenceRange'] as String?,
      result: json['result'] as String? ?? '',
      doctorNote: json['doctorNote'] as String?,
      fileName: json['fileName'] as String?,
      contentType: json['contentType'] as String?,
      fileSizeBytes: json['fileSizeBytes'] as int? ?? 0,
      enteredByName: json['enteredByName'] as String? ?? '',
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      downloadUrl: json['downloadUrl'] as String? ?? '',
      hasFile: json['hasFile'] as bool? ?? false,
    );
  }
}
