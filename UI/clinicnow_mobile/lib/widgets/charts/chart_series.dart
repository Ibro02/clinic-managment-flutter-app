import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

/// One line, area or bar series.
///
/// Colour is optional: leave it null and the chart assigns from the palette in
/// order, which keeps series colours consistent across every chart in the app
/// without any screen picking hex values.
class AppChartSeries {
  final String name;
  final List<double> values;
  final Color? color;

  const AppChartSeries({required this.name, required this.values, this.color});
}

/// One slice of a donut.
class AppChartSlice {
  final String name;
  final double value;
  final Color? color;

  const AppChartSlice({required this.name, required this.value, this.color});
}

/// Axis and tooltip number formatting.
///
/// Compact form matters on a y-axis: "12.4k" keeps the axis gutter narrow, and
/// a narrow gutter leaves more width for the data itself.
abstract final class ChartFormat {
  static final NumberFormat _decimal = NumberFormat.decimalPattern('bs');

  static String count(double value) => _decimal.format(value.round());

  static String money(double value) => '${_decimal.format(value.round())} KM';

  static String compact(double value) {
    final v = value.abs();
    if (v >= 1000000) return '${(value / 1000000).toStringAsFixed(v % 1000000 == 0 ? 0 : 1)}M';
    if (v >= 1000) return '${(value / 1000).toStringAsFixed(v % 1000 == 0 ? 0 : 1)}k';
    return value.round().toString();
  }

  /// A "nice" axis maximum: the next round number above the data, so gridlines
  /// land on values a person can read rather than on 8,437.
  static double niceMax(double rawMax) {
    if (rawMax <= 0) return 10;
    final padded = rawMax * 1.15;
    final magnitude = _pow10((padded.abs()).floor().toString().length - 1);
    return (padded / magnitude).ceil() * magnitude;
  }

  static double _pow10(int exponent) {
    var result = 1.0;
    for (var i = 0; i < exponent; i++) {
      result *= 10;
    }
    return result;
  }
}
