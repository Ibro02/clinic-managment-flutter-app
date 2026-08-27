import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'chart_series.dart';

/// A smoothed line chart with a gradient fill under the primary series.
///
/// Choices that separate this from fl_chart's defaults:
///   * Horizontal gridlines only, in a barely-there tint. Vertical gridlines on
///     a time series add a second grid the eye has to filter out for no
///     information gain.
///   * No axis border. The gridlines already establish the plane; a box around
///     it is one more line for nothing.
///   * Dots hidden until touched, then a dashed crosshair drops to the axis.
///     Permanent dots on a 30-point series turn the line into a caterpillar.
///   * A compact y-axis ("12.4k") so the gutter stays narrow.
class AppAreaChart extends StatelessWidget {
  final List<AppChartSeries> series;
  final List<String> labels;
  final double height;

  /// Formats tooltip values. The axis always uses the compact form.
  final String Function(double value) valueFormatter;

  /// Fills the area under the first series. Turn off when comparing two series,
  /// where two overlapping fills muddy each other.
  final bool filled;

  const AppAreaChart({
    super.key,
    required this.series,
    required this.labels,
    this.height = 260,
    this.valueFormatter = ChartFormat.count,
    this.filled = true,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    final maxValue = series
        .expand((s) => s.values)
        .fold<double>(0, (previous, value) => value > previous ? value : previous);
    final maxY = ChartFormat.niceMax(maxValue);
    final horizontalInterval = maxY / 4;

    // Show at most seven x labels; past that they collide and start rotating,
    // which is the point where a chart stops looking designed.
    final labelStep = (labels.length / 7).ceil().clamp(1, 999);

    return SizedBox(
      height: height,
      child: LineChart(
        LineChartData(
          minY: 0,
          maxY: maxY,
          minX: 0,
          maxX: (labels.length - 1).toDouble(),
          clipData: FlClipData.all(),
          gridData: FlGridData(
            show: true,
            drawVerticalLine: false,
            horizontalInterval: horizontalInterval,
            getDrawingHorizontalLine: (value) => FlLine(
              color: c.chartGrid,
              strokeWidth: 1,
              dashArray: value == 0 ? null : [4, 4],
            ),
          ),
          borderData: FlBorderData(show: false),
          titlesData: FlTitlesData(
            topTitles: const AxisTitles(),
            rightTitles: const AxisTitles(),
            leftTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 42,
                interval: horizontalInterval,
                getTitlesWidget: (value, meta) => Padding(
                  padding: const EdgeInsets.only(right: AppSpacing.xs),
                  child: Text(
                    ChartFormat.compact(value),
                    textAlign: TextAlign.right,
                    style: context.text.labelSmall?.copyWith(
                      color: c.textMuted,
                      letterSpacing: 0,
                      fontSize: 11,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ),
              ),
            ),
            bottomTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 30,
                interval: 1,
                getTitlesWidget: (value, meta) {
                  final index = value.round();
                  if (index < 0 || index >= labels.length) return const SizedBox.shrink();
                  if (index % labelStep != 0) return const SizedBox.shrink();

                  return Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.xs),
                    child: Text(
                      labels[index],
                      style: context.text.labelSmall?.copyWith(
                        color: c.textMuted,
                        letterSpacing: 0,
                        fontSize: 11,
                      ),
                    ),
                  );
                },
              ),
            ),
          ),
          lineTouchData: LineTouchData(
            handleBuiltInTouches: true,
            touchTooltipData: LineTouchTooltipData(
              getTooltipColor: (_) => c.textPrimary,
              tooltipRoundedRadius: AppRadius.sm,
              tooltipPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              tooltipMargin: 12,
              fitInsideHorizontally: true,
              fitInsideVertically: true,
              getTooltipItems: (touchedSpots) {
                return touchedSpots.asMap().entries.map((entry) {
                  final spot = entry.value;
                  final name = series[spot.barIndex].name;
                  final index = spot.x.round();
                  final header = index >= 0 && index < labels.length ? labels[index] : '';

                  final valueStyle = TextStyle(
                    color: c.surface,
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                    fontFeatures: AppTypography.tabular,
                  );

                  return LineTooltipItem(
                    entry.key == 0 ? '$header\n' : '',
                    TextStyle(
                      color: c.surface.withValues(alpha: 0.6),
                      fontSize: 11,
                      fontWeight: FontWeight.w500,
                    ),
                    children: [
                      TextSpan(text: '$name  ', style: valueStyle.copyWith(fontWeight: FontWeight.w400)),
                      TextSpan(text: valueFormatter(spot.y), style: valueStyle),
                    ],
                  );
                }).toList();
              },
            ),
            getTouchedSpotIndicator: (barData, indexes) => indexes
                .map(
                  (_) => TouchedSpotIndicatorData(
                    FlLine(color: c.borderStrong, strokeWidth: 1, dashArray: [4, 4]),
                    FlDotData(
                      show: true,
                      getDotPainter: (spot, percent, bar, index) => FlDotCirclePainter(
                        radius: 4.5,
                        color: bar.color ?? c.primary,
                        strokeWidth: 2.5,
                        strokeColor: c.surface,
                      ),
                    ),
                  ),
                )
                .toList(),
          ),
          lineBarsData: [
            for (var i = 0; i < series.length; i++) _bar(context, series[i], i),
          ],
        ),
      ),
    );
  }

  LineChartBarData _bar(BuildContext context, AppChartSeries s, int index) {
    final c = context.colors;
    final color = s.color ?? c.series(index);

    return LineChartBarData(
      spots: [
        for (var i = 0; i < s.values.length; i++) FlSpot(i.toDouble(), s.values[i]),
      ],
      isCurved: true,
      curveSmoothness: 0.28,
      preventCurveOverShooting: true,
      color: color,
      barWidth: 2.4,
      isStrokeCapRound: true,
      dotData: const FlDotData(show: false),
      belowBarData: BarAreaData(
        show: filled && index == 0,
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [
            color.withValues(alpha: 0.26),
            color.withValues(alpha: 0.02),
          ],
        ),
      ),
    );
  }
}
