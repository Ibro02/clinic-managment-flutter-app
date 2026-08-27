import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';

/// A tiny axis-free trend line for a KPI tile.
///
/// It carries no exact values on purpose. The number above it is the fact; the
/// sparkline only answers "and which way is it going", which is the one thing a
/// single number can never tell you.
class AppSparkline extends StatelessWidget {
  final List<double> values;
  final Color? color;
  final double height;

  const AppSparkline({super.key, required this.values, this.color, this.height = 38});

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final resolved = color ?? c.primary;

    if (values.length < 2) return SizedBox(height: height);

    final min = values.reduce((a, b) => a < b ? a : b);
    final max = values.reduce((a, b) => a > b ? a : b);
    // A flat series would otherwise collapse onto the top edge; the padding
    // keeps it drawn through the middle.
    final padding = (max - min).abs() < 0.001 ? (max.abs() * 0.1 + 1) : (max - min) * 0.18;

    return SizedBox(
      height: height,
      child: LineChart(
        LineChartData(
          minY: min - padding,
          maxY: max + padding,
          minX: 0,
          maxX: (values.length - 1).toDouble(),
          gridData: const FlGridData(show: false),
          titlesData: const FlTitlesData(show: false),
          borderData: FlBorderData(show: false),
          lineTouchData: const LineTouchData(enabled: false),
          lineBarsData: [
            LineChartBarData(
              spots: [
                for (var i = 0; i < values.length; i++) FlSpot(i.toDouble(), values[i]),
              ],
              isCurved: true,
              curveSmoothness: 0.3,
              preventCurveOverShooting: true,
              color: resolved,
              barWidth: 2,
              isStrokeCapRound: true,
              dotData: const FlDotData(show: false),
              belowBarData: BarAreaData(
                show: true,
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [
                    resolved.withValues(alpha: 0.22),
                    resolved.withValues(alpha: 0.0),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
