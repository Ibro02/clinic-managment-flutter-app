import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import '../ui/app_badge.dart';
import 'chart_series.dart';

/// The frame every chart sits in: title, an optional headline number, a
/// period-over-period delta, a control on the right, then the plot.
///
/// Putting the headline value inside the chart card rather than in a separate
/// tile is the point. A chart shows shape; the number shows magnitude. Split
/// across two cards, a person has to look twice and connect them themselves.
class ChartCard extends StatelessWidget {
  final String title;
  final String? subtitle;
  final String? value;
  final double? deltaPercent;
  final Widget? trailing;
  final List<AppChartSeries> legend;
  final Widget child;

  /// Set false when a rise is bad news (cancellations, waiting time), so the
  /// delta colours flip. Green for "more cancellations" is an easy mistake and
  /// an expensive one on a dashboard someone reads at a glance.
  final bool higherIsBetter;

  const ChartCard({
    super.key,
    required this.title,
    required this.child,
    this.subtitle,
    this.value,
    this.deltaPercent,
    this.trailing,
    this.legend = const [],
    this.higherIsBetter = true,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return DecoratedBox(
      decoration: BoxDecoration(
        color: c.surface,
        borderRadius: AppRadius.all(AppRadius.lg),
        border: Border.all(color: c.border),
        boxShadow: c.shadowSm,
      ),
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(title, style: context.text.titleMedium),
                      if (subtitle != null)
                        Padding(
                          padding: const EdgeInsets.only(top: 2),
                          child: Text(
                            subtitle!,
                            style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12.5),
                          ),
                        ),
                    ],
                  ),
                ),
                if (trailing != null) trailing!,
              ],
            ),
            if (value != null) ...[
              const SizedBox(height: AppSpacing.md),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    value!,
                    style: context.text.headlineLarge?.copyWith(fontFeatures: AppTypography.tabular),
                  ),
                  if (deltaPercent != null) ...[
                    const SizedBox(width: AppSpacing.sm),
                    Padding(
                      padding: const EdgeInsets.only(bottom: 5),
                      child: DeltaBadge(percent: deltaPercent!, higherIsBetter: higherIsBetter),
                    ),
                  ],
                ],
              ),
            ],
            if (legend.isNotEmpty) ...[
              const SizedBox(height: AppSpacing.sm),
              Wrap(
                spacing: AppSpacing.md,
                runSpacing: AppSpacing.xs,
                children: [
                  for (var i = 0; i < legend.length; i++) _legendItem(context, legend[i], i),
                ],
              ),
            ],
            const SizedBox(height: AppSpacing.lg),
            child,
          ],
        ),
      ),
    );
  }

  Widget _legendItem(BuildContext context, AppChartSeries s, int index) {
    final c = context.colors;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 8,
          height: 8,
          decoration: BoxDecoration(
            color: s.color ?? c.series(index),
            borderRadius: AppRadius.all(3),
          ),
        ),
        const SizedBox(width: 6),
        Text(s.name, style: context.text.bodySmall?.copyWith(color: c.textSecondary, fontSize: 12.5)),
      ],
    );
  }
}

/// A signed percentage change with an arrow, coloured by whether the direction
/// is good news rather than by its sign.
class DeltaBadge extends StatelessWidget {
  final double percent;
  final bool higherIsBetter;

  const DeltaBadge({super.key, required this.percent, this.higherIsBetter = true});

  @override
  Widget build(BuildContext context) {
    final rising = percent >= 0;
    final good = rising == higherIsBetter;
    final tone = percent.abs() < 0.05
        ? AppTone.neutral
        : good
            ? AppTone.success
            : AppTone.danger;

    return AppStatusBadge(
      label: '${rising ? '+' : ''}${percent.toStringAsFixed(1)}%',
      tone: tone,
      icon: rising ? Icons.trending_up_rounded : Icons.trending_down_rounded,
    );
  }
}
