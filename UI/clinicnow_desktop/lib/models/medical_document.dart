/// Mirrors the backend's `MedicalDocumentDto`. Never carries raw file bytes -
/// only [downloadUrl] (the actual bytes are served from a dedicated endpoint).
class MedicalDocument {
  final int id;
  final int patientId;
  final String patientName;
  final String fileName;
  final String contentType;
  final int fileSizeBytes;
  final String? description;
  final String uploadedByName;
  final DateTime createdAtUtc;
  final String downloadUrl;

  const MedicalDocument({
    required this.id,
    required this.patientId,
    required this.patientName,
    required this.fileName,
    required this.contentType,
    required this.fileSizeBytes,
    this.description,
    required this.uploadedByName,
    required this.createdAtUtc,
    required this.downloadUrl,
  });

  factory MedicalDocument.fromJson(Map<String, dynamic> json) {
    return MedicalDocument(
      id: json['id'] as int,
      patientId: json['patientId'] as int,
      patientName: json['patientName'] as String? ?? '',
      fileName: json['fileName'] as String? ?? '',
      contentType: json['contentType'] as String? ?? '',
      fileSizeBytes: json['fileSizeBytes'] as int? ?? 0,
      description: json['description'] as String?,
      uploadedByName: json['uploadedByName'] as String? ?? '',
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      downloadUrl: json['downloadUrl'] as String? ?? '',
    );
  }
}
