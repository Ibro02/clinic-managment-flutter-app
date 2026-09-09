/// Mirrors the backend's `DiagnosisDto` - the ICD-10 diagnosis codebook a
/// medical-record entry references instead of carrying free text.
class Diagnosis {
  final int id;
  final String code;
  final String name;
  final int? suggestedSpecializationId;
  final String? suggestedSpecializationName;

  const Diagnosis({
    required this.id,
    required this.code,
    required this.name,
    this.suggestedSpecializationId,
    this.suggestedSpecializationName,
  });

  /// "J06.9 - Akutna infekcija gornjih disajnih puteva" - what dropdowns and
  /// tables show; the raw id is never rendered (rulebook §6).
  String get displayName => '$code - $name';

  factory Diagnosis.fromJson(Map<String, dynamic> json) => Diagnosis(
        id: json['id'] as int,
        code: json['code'] as String? ?? '',
        name: json['name'] as String? ?? '',
        suggestedSpecializationId: json['suggestedSpecializationId'] as int?,
        suggestedSpecializationName: json['suggestedSpecializationName'] as String?,
      );

  /// Value equality so a `FormBuilderDropdown` can match its initial value
  /// against a freshly-fetched list instance.
  @override
  bool operator ==(Object other) => other is Diagnosis && other.id == id;

  @override
  int get hashCode => id.hashCode;
}
