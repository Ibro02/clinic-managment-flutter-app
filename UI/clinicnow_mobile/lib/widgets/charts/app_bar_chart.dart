import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'chart_series.dart';

/// A bar chart with rounded tops and a faint background track behind each bar.
///
/// The background track is the detail that makes this read as finished: it
/// shows each bar's headroom against the axis maximum, so a short bar looks
/// deliberately short rather than like a rendering accident.
///
/// Grouped series are supported, but three or more groups per category get
/// unreadable fast - prefer a line chart past two.
class AppBarChart extends StatelessWidget {
  final List<AppChartSeries> series;
  final List<String> labels;
  final double height;
  final String Function(double value) valueFormatter;
  final bool showTrack;

  const AppBarChart({
    super.key,
    required this.series,
    required this.labels,
    this.height = 260,
    this.valueFormatter = ChartFormat.count,
    this.showTrack = true,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    final maxValue = series
        .expand((s) => s.values)
        .fold<double>(0, (previous, value) => value > previous ? value : previous);
    final maxY = ChartFormat.niceMax(maxValue);
    final horizontalInterval = maxY / 4;

    final barWidth = series.length > 1 ? 12.0 : 18.0;

    return SizedBox(
      height: height,
      child: BarChart(
        BarChartData(
          maxY: maxY,
          alignment: BarChartAlignment.spaceAround,
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
                getTitlesWidget: (value, meta) {
                  final index = value.round();
                  if (index < 0 || index >= labels.length) return const SizedBox.shrink();

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
          barTouchData: BarTouchData(
            touchTooltipData: BarTouchTooltipData(
              getTooltipColor: (_) => c.textPrimary,
              tooltipRoundedRadius: AppRadius.sm,
              tooltipPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              tooltipMargin: 10,
              fitInsideHorizontally: true,
              fitInsideVertically: true,
              getTooltipItem: (group, groupIndex, rod, rodIndex) {
                final name = series[rodIndex].name;
                final header = groupIndex < labels.length ? labels[groupIndex] : '';

                return BarTooltipItem(
                  '$header\n',
                  TextStyle(
                    color: c.surface.withValues(alpha: 0.6),
                    fontSize: 11,
                    fontWeight: FontWeight.w500,
                  ),
                  children: [
                    TextSpan(
                      text: '$name  ',
                      style: TextStyle(color: c.surface, fontSize: 12.5, fontWeight: FontWeight.w400),
                    ),
                    TextSpan(
                      text: valueFormatter(rod.toY),
                      style: TextStyle(
                        color: c.surface,
                        fontSize: 12.5,
                        fontWeight: FontWeight.w600,
                        fontFeatures: AppTypography.tabular,
                      ),
                    ),
                  ],
                );
              },
            ),
          ),
          barGroups: [
            for (var groupIndex = 0; groupIndex < labels.length; groupIndex++)
              BarChartGroupData(
                x: groupIndex,
                barsSpace: 5,
                barRods: [
                  for (var seriesIndex = 0; seriesIndex < series.length; seriesIndex++)
                    BarChartRodData(
                      toY: groupIndex < series[seriesIndex].values.length
                          ? series[seriesIndex].values[groupIndex]
                          : 0,
                      width: barWidth,
                      borderRadius: const BorderRadius.vertical(top: Radius.circular(5)),
                      gradient: _rodGradient(context, seriesIndex),
                      backDrawRodData: BackgroundBarChartRodData(
                        show: showTrack,
                        toY: maxY,
                        color: c.surfaceMuted,
                      ),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }

  /// A vertical gradient rather than a flat fill: the bar reads as lit from
  /// above, which keeps a wall of solid rectangles from looking like a
  /// spreadsheet chart.
  LinearGradient _rodGradient(BuildContext context, int index) {
    final base = series[index].color ?? context.colors.series(index);

    return LinearGradient(
      begin: Alignment.bottomCenter,
      end: Alignment.topCenter,
      colors: [base.withValues(alpha: 0.72), base],
    );
  }
}
