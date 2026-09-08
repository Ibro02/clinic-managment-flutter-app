/// Mirrors the backend's `AppointmentAuditLogDto` - one status-change entry
/// (who, when, why). Only ever populated by the detail endpoint
/// (`AppointmentProvider.getById`, review item 11/rulebook §7); the paged
/// list leaves it empty, matching the backend's own list-vs-detail split.
class AppointmentAuditLog {
  final int id;
  final String actingUserName;
  final String statusName;
  final DateTime occurredAtUtc;
  final String? description;

  AppointmentAuditLog({
    required this.id,
    required this.actingUserName,
    required this.statusName,
    required this.occurredAtUtc,
    this.description,
  });

  factory AppointmentAuditLog.fromJson(Map<String, dynamic> json) => AppointmentAuditLog(
        id: json['id'] as int,
        actingUserName: json['actingUserName'] as String,
        statusName: json['statusName'] as String,
        occurredAtUtc: DateTime.parse(json['occurredAtUtc'] as String).toLocal(),
        description: json['description'] as String?,
      );
}

/// Mirrors the backend's `AppointmentDto`. `status` matches .NET's
/// `AppointmentStatus` enum (0=Pending, 1=Confirmed, 2=Completed, 3=Cancelled).
class Appointment {
  final int id;
  final int patientId;
  final String patientName;
  final int doctorId;
  final String doctorName;
  final int medicalServiceId;
  final String medicalServiceName;
  final int locationId;
  final String locationName;
  final DateTime startUtc;
  final DateTime endUtc;
  final int status;
  final String statusName;
  final String? cancellationReason;
  final DateTime createdAtUtc;

  /// Legal next actions from the current status ("Confirm"/"Complete"/"Cancel") -
  /// computed server-side by the state machine, so the UI never has to
  /// reimplement "what's legal from this status" itself (rulebook Part II §K).
  final List<String> allowedActions;

  final bool isPaid;
  final String? paymentStatus;
  final int? paymentId;
  final bool canRefund;

  /// Cancelled, but the automatic refund failed and the money is still owed
  /// to the patient (review item C14). Staff clear it by retrying the refund.
  final bool refundFailed;

  /// Full status-change history - empty unless this came from
  /// `AppointmentProvider.getById` (see [AppointmentAuditLog]).
  final List<AppointmentAuditLog> auditLogs;

  Appointment({
    required this.id,
    required this.patientId,
    required this.patientName,
    required this.doctorId,
    required this.doctorName,
    required this.medicalServiceId,
    required this.medicalServiceName,
    required this.locationId,
    required this.locationName,
    required this.startUtc,
    required this.endUtc,
    required this.status,
    required this.statusName,
    this.cancellationReason,
    required this.createdAtUtc,
    required this.allowedActions,
    required this.isPaid,
    this.paymentStatus,
    this.paymentId,
    required this.canRefund,
    this.refundFailed = false,
    this.auditLogs = const [],
  });

  bool get canConfirm => allowedActions.contains('Confirm');
  bool get canComplete => allowedActions.contains('Complete');
  bool get canCancel => allowedActions.contains('Cancel');
  bool get canReschedule => allowedActions.contains('Reschedule');

  factory Appointment.fromJson(Map<String, dynamic> json) => Appointment(
        id: json['id'] as int,
        patientId: json['patientId'] as int,
        patientName: json['patientName'] as String,
        doctorId: json['doctorId'] as int,
        doctorName: json['doctorName'] as String,
        medicalServiceId: json['medicalServiceId'] as int,
        medicalServiceName: json['medicalServiceName'] as String,
        locationId: json['locationId'] as int,
        locationName: json['locationName'] as String,
        startUtc: DateTime.parse(json['startUtc'] as String).toLocal(),
        endUtc: DateTime.parse(json['endUtc'] as String).toLocal(),
        status: json['status'] as int,
        statusName: json['statusName'] as String,
        cancellationReason: json['cancellationReason'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toLocal(),
        allowedActions: (json['allowedActions'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
        // Payment state is read defensively: an API build predating the
        // payments phase omits these keys entirely, and a hard `as bool` on an
        // absent key throws a TypeError that takes down the parse of the whole
        // page - one missing optional field would otherwise blank the entire
        // appointments list. Absent payment state means "not paid yet".
        isPaid: json['isPaid'] as bool? ?? false,
        paymentStatus: json['paymentStatus'] as String?,
        paymentId: json['paymentId'] as int?,
        canRefund: json['canRefund'] as bool? ?? false,
        refundFailed: json['refundFailed'] as bool? ?? false,
        auditLogs: (json['auditLogs'] as List<dynamic>? ?? [])
            .map((e) => AppointmentAuditLog.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
