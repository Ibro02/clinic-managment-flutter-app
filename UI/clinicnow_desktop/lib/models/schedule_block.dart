/// Mirrors the backend's `ScheduleBlockDto`. Times are UTC (CLAUDE.md "Time
/// Handling" - convert to local only for display).
class ScheduleBlock {
  final int id;
  final int doctorId;
  final String doctorName;
  final DateTime startUtc;
  final DateTime endUtc;
  final String reason;

  ScheduleBlock({
    required this.id,
    required this.doctorId,
    required this.doctorName,
    required this.startUtc,
    required this.endUtc,
    required this.reason,
  });

  factory ScheduleBlock.fromJson(Map<String, dynamic> json) => ScheduleBlock(
        id: json['id'] as int,
        doctorId: json['doctorId'] as int,
        doctorName: json['doctorName'] as String,
        startUtc: DateTime.parse(json['startUtc'] as String).toLocal(),
        endUtc: DateTime.parse(json['endUtc'] as String).toLocal(),
        reason: json['reason'] as String,
      );
}
