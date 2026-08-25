import 'package:flutter/material.dart';

/// Mirrors the backend's `WorkingHoursDto`. `dayOfWeek` matches .NET's
/// `System.DayOfWeek` enum values (0 = Sunday .. 6 = Saturday).
class WorkingHours {
  final int id;
  final int doctorId;
  final String doctorName;
  final int dayOfWeek;
  final TimeOfDay startTime;
  final TimeOfDay endTime;

  WorkingHours({
    required this.id,
    required this.doctorId,
    required this.doctorName,
    required this.dayOfWeek,
    required this.startTime,
    required this.endTime,
  });

  factory WorkingHours.fromJson(Map<String, dynamic> json) => WorkingHours(
        id: json['id'] as int,
        doctorId: json['doctorId'] as int,
        doctorName: json['doctorName'] as String,
        dayOfWeek: json['dayOfWeek'] as int,
        startTime: _parseTime(json['startTime'] as String),
        endTime: _parseTime(json['endTime'] as String),
      );

  static TimeOfDay _parseTime(String value) {
    final parts = value.split(':');
    return TimeOfDay(hour: int.parse(parts[0]), minute: int.parse(parts[1]));
  }
}

/// Bosnian day-of-week labels indexed by .NET's `DayOfWeek` (0 = Sunday).
const List<String> kDayOfWeekNames = [
  'Nedjelja',
  'Ponedjeljak',
  'Utorak',
  'Srijeda',
  'Četvrtak',
  'Petak',
  'Subota',
];

String formatTimeOfDay(TimeOfDay time) =>
    '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';

String timeOfDayToApi(TimeOfDay time) => '${formatTimeOfDay(time)}:00';
