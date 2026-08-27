import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'chart_series.dart';

/// A donut with the total in the middle and a legend beside it.
///
/// The hole is not decoration - it is where the number goes. A pie chart makes
/// a person compare wedge areas, which humans are bad at; a donut with a
/// legible total plus a legend carrying exact values and percentages gives them
/// the shape *and* the numbers, and the shape only has to convey "roughly how
/// split is this".
///
/// Slice labels are deliberately absent. Text rotated inside a wedge is the
/// fastest way to make a chart look amateur.
class AppDonutChart extends StatefulWidget {
  final List<AppChartSlice> slices;
  final String centerLabel;
  final double size;
  final String Function(double value) valueFormatter;

  const AppDonutChart({
    super.key,
    required this.slices,
    this.centerLabel = 'Ukupno',
    this.size = 200,
    this.valueFormatter = ChartFormat.count,
  });

  @override
  State<AppDonutChart> createState() => _AppDonutChartState();
}

class _AppDonutChartState extends State<AppDonutChart> {
  int _touchedIndex = -1;

  double get _total => widget.slices.fold<double>(0, (sum, slice) => sum + slice.value);

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        // Below 460px the legend has nowhere to go, so it moves under the
        // donut rather than squeezing both into an unreadable strip.
        final stacked = constraints.maxWidth < 460;

        final chart = SizedBox(
          height: widget.size,
          width: widget.size,
          child: Stack(
            alignment: Alignment.center,
            children: [
              PieChart(
                PieChartData(
                  sectionsSpace: 3,
                  centerSpaceRadius: widget.size * 0.3,
                  startDegreeOffset: -90,
                  sections: _sections(context),
                  pieTouchData: PieTouchData(
                    touchCallback: (event, response) {
                      setState(() {
                        _touchedIndex = response?.touchedSection?.touchedSectionIndex ?? -1;
                      });
                    },
                  ),
                ),
              ),
              _center(context),
            ],
          ),
        );

        final legend = _legend(context);

        if (stacked) {
          return Column(
            children: [chart, const SizedBox(height: AppSpacing.md), legend],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            chart,
            const SizedBox(width: AppSpacing.lg),
            Expanded(child: legend),
          ],
        );
      },
    );
  }

  List<PieChartSectionData> _sections(BuildContext context) {
    final c = context.colors;

    return [
      for (var i = 0; i < widget.slices.length; i++)
        PieChartSectionData(
          value: widget.slices[i].value,
          color: widget.slices[i].color ?? c.series(i),
          // The touched slice grows a few pixels instead of exploding outward -
          // enough to confirm the hover, not enough to make the ring jump.
          radius: _touchedIndex == i ? widget.size * 0.24 : widget.size * 0.2,
          showTitle: false,
          borderSide: BorderSide(color: c.surface, width: 2),
        ),
    ];
  }

  Widget _center(BuildContext context) {
    final c = context.colors;
    final highlighted = _touchedIndex >= 0 && _touchedIndex < widget.slices.length;
    final slice = highlighted ? widget.slices[_touchedIndex] : null;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          slice?.name ?? widget.centerLabel,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: context.text.labelSmall?.copyWith(color: c.textMuted, fontSize: 10.5),
        ),
        const SizedBox(height: 2),
        Text(
          widget.valueFormatter(slice?.value ?? _total),
          style: context.text.headlineSmall?.copyWith(
            fontSize: 22,
            fontFeatures: AppTypography.tabular,
          ),
        ),
      ],
    );
  }

  Widget _legend(BuildContext context) {
    final c = context.colors;

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < widget.slices.length; i++)
          MouseRegion(
            onEnter: (_) => setState(() => _touchedIndex = i),
            onExit: (_) => setState(() => _touchedIndex = -1),
            child: AnimatedContainer(
              duration: AppDuration.fast,
              margin: const EdgeInsets.only(bottom: 2),
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs, vertical: 7),
              decoration: BoxDecoration(
                color: _touchedIndex == i ? c.surfaceHover : Colors.transparent,
                borderRadius: AppRadius.all(AppRadius.sm),
              ),
              child: Row(
                children: [
                  Container(
                    width: 9,
                    height: 9,
                    decoration: BoxDecoration(
                      color: widget.slices[i].color ?? c.series(i),
                      borderRadius: AppRadius.all(3),
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Expanded(
                    child: Text(
                      widget.slices[i].name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: context.text.bodySmall?.copyWith(color: c.textSecondary),
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Text(
                    widget.valueFormatter(widget.slices[i].value),
                    style: context.text.titleSmall?.copyWith(
                      fontSize: 13,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                  SizedBox(
                    width: 46,
                    child: Text(
                      _total == 0 ? '' : '${(widget.slices[i].value / _total * 100).toStringAsFixed(0)}%',
                      textAlign: TextAlign.right,
                      style: context.text.bodySmall?.copyWith(
                        color: c.textMuted,
                        fontSize: 12,
                        fontFeatures: AppTypography.tabular,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}
