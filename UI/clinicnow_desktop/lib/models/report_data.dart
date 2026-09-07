/// Mirrors `ClinicNow.Model.Reports.RevenueReportData` /
/// `AppointmentsReportData` - the aggregate figures behind each PDF, so the
/// report tabs can chart what the document says (review item C8) instead of
/// re-deriving it from data the client doesn't have.
class RevenueReportRow {
  final String serviceName;
  final int paymentCount;

  /// Captured minus refunded - a refunded visit isn't revenue.
  final double netTotalEur;

  const RevenueReportRow({
    required this.serviceName,
    required this.paymentCount,
    required this.netTotalEur,
  });

  factory RevenueReportRow.fromJson(Map<String, dynamic> json) => RevenueReportRow(
        serviceName: json['serviceName'] as String? ?? '',
        paymentCount: json['paymentCount'] as int? ?? 0,
        netTotalEur: (json['netTotalEur'] as num?)?.toDouble() ?? 0,
      );
}

class RevenueReportData {
  final List<RevenueReportRow> rows;
  final double grandTotalEur;

  const RevenueReportData({required this.rows, required this.grandTotalEur});

  factory RevenueReportData.fromJson(Map<String, dynamic> json) => RevenueReportData(
        rows: (json['rows'] as List<dynamic>? ?? [])
            .map((e) => RevenueReportRow.fromJson(e as Map<String, dynamic>))
            .toList(),
        grandTotalEur: (json['grandTotalEur'] as num?)?.toDouble() ?? 0,
      );
}

class AppointmentsReportRow {
  final String doctorName;
  final int pendingCount;
  final int confirmedCount;
  final int completedCount;
  final int cancelledCount;
  final int totalCount;

  const AppointmentsReportRow({
    required this.doctorName,
    required this.pendingCount,
    required this.confirmedCount,
    required this.completedCount,
    required this.cancelledCount,
    required this.totalCount,
  });

  factory AppointmentsReportRow.fromJson(Map<String, dynamic> json) => AppointmentsReportRow(
        doctorName: json['doctorName'] as String? ?? '',
        pendingCount: json['pendingCount'] as int? ?? 0,
        confirmedCount: json['confirmedCount'] as int? ?? 0,
        completedCount: json['completedCount'] as int? ?? 0,
        cancelledCount: json['cancelledCount'] as int? ?? 0,
        totalCount: json['totalCount'] as int? ?? 0,
      );
}

class AppointmentsReportData {
  final List<AppointmentsReportRow> rows;
  final int totalCount;

  const AppointmentsReportData({required this.rows, required this.totalCount});

  factory AppointmentsReportData.fromJson(Map<String, dynamic> json) => AppointmentsReportData(
        rows: (json['rows'] as List<dynamic>? ?? [])
            .map((e) => AppointmentsReportRow.fromJson(e as Map<String, dynamic>))
            .toList(),
        totalCount: json['totalCount'] as int? ?? 0,
      );
}
