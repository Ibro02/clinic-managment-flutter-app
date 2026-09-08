/// Mirrors the backend's `AppointmentStatusInfoDto` (review item S2) - one row
/// of the read-only "Statusi termina" codebook tab. `allowedActions` comes
/// straight from the state machine's `AllowedActions()`, so this list can
/// never drift from what the backend actually enforces.
class AppointmentStatusInfo {
  final int status;
  final String statusName;
  final String description;
  final List<String> allowedActions;

  AppointmentStatusInfo({
    required this.status,
    required this.statusName,
    required this.description,
    required this.allowedActions,
  });

  factory AppointmentStatusInfo.fromJson(Map<String, dynamic> json) => AppointmentStatusInfo(
        status: json['status'] as int,
        statusName: json['statusName'] as String,
        description: json['description'] as String,
        allowedActions: (json['allowedActions'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
      );
}
