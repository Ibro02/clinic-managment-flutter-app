/// Mirrors the backend's `WeeklyTrendPointDto`.
class WeeklyTrendPoint {
  final DateTime date;
  final int appointmentCount;

  WeeklyTrendPoint({required this.date, required this.appointmentCount});

  factory WeeklyTrendPoint.fromJson(Map<String, dynamic> json) => WeeklyTrendPoint(
        date: DateTime.parse(json['date'] as String),
        appointmentCount: json['appointmentCount'] as int,
      );
}

/// Mirrors the backend's `DashboardSummaryDto`.
class DashboardSummary {
  final int todayAppointmentsCount;
  final int activePatientsCount;
  final int availableDoctorsCount;
  final double monthlyRevenueEur;
  final List<WeeklyTrendPoint> weeklyTrend;
  final int newPatientsCount30d;
  final int existingPatientsCount30d;

  DashboardSummary({
    required this.todayAppointmentsCount,
    required this.activePatientsCount,
    required this.availableDoctorsCount,
    required this.monthlyRevenueEur,
    required this.weeklyTrend,
    required this.newPatientsCount30d,
    required this.existingPatientsCount30d,
  });

  factory DashboardSummary.fromJson(Map<String, dynamic> json) => DashboardSummary(
        todayAppointmentsCount: json['todayAppointmentsCount'] as int,
        activePatientsCount: json['activePatientsCount'] as int,
        availableDoctorsCount: json['availableDoctorsCount'] as int,
        monthlyRevenueEur: (json['monthlyRevenueEur'] as num).toDouble(),
        weeklyTrend: (json['weeklyTrend'] as List<dynamic>)
            .map((e) => WeeklyTrendPoint.fromJson(e as Map<String, dynamic>))
            .toList(),
        newPatientsCount30d: json['newPatientsCount30d'] as int,
        existingPatientsCount30d: json['existingPatientsCount30d'] as int,
      );
}
