/// Mirrors the backend's `AppointmentRecommendationDto`.
class AppointmentRecommendation {
  final int doctorId;
  final String doctorName;
  final List<String> doctorSpecializations;
  final int medicalServiceId;
  final String medicalServiceName;
  final double medicalServicePrice;
  final int locationId;
  final String locationName;
  final DateTime suggestedStartUtc;
  final double score;
  final String reason;
  final bool isPopularityFallback;

  AppointmentRecommendation({
    required this.doctorId,
    required this.doctorName,
    required this.doctorSpecializations,
    required this.medicalServiceId,
    required this.medicalServiceName,
    required this.medicalServicePrice,
    required this.locationId,
    required this.locationName,
    required this.suggestedStartUtc,
    required this.score,
    required this.reason,
    required this.isPopularityFallback,
  });

  factory AppointmentRecommendation.fromJson(Map<String, dynamic> json) => AppointmentRecommendation(
        doctorId: json['doctorId'] as int,
        doctorName: json['doctorName'] as String,
        doctorSpecializations: (json['doctorSpecializations'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
        medicalServiceId: json['medicalServiceId'] as int,
        medicalServiceName: json['medicalServiceName'] as String,
        medicalServicePrice: (json['medicalServicePrice'] as num).toDouble(),
        locationId: json['locationId'] as int,
        locationName: json['locationName'] as String,
        suggestedStartUtc: DateTime.parse(json['suggestedStartUtc'] as String),
        score: (json['score'] as num).toDouble(),
        reason: json['reason'] as String,
        isPopularityFallback: json['isPopularityFallback'] as bool,
      );
}

/// Matches the backend's `InteractionType` enum (0=DoctorView, 1=MedicalServiceView, 2=Search) - sent as an int on the wire, same convention as `Appointment.status`.
enum InteractionType { doctorView, medicalServiceView, search }
