import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import '../charts/chart_card.dart';
import '../charts/spark_line.dart';
import 'app_badge.dart';

/// A single KPI tile: label, number, direction, and a trend line.
///
/// The number is set in the display face at 30px with tight tracking, which is
/// the one place in the app where type is allowed to be loud. Everything else
/// on the tile is quiet so the figure carries it.
class StatCard extends StatefulWidget {
  final String label;
  final String value;
  final IconData icon;
  final AppTone tone;
  final double? deltaPercent;
  final bool higherIsBetter;
  final List<double>? trend;
  final String? caption;
  final VoidCallback? onTap;

  const StatCard({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    this.tone = AppTone.primary,
    this.deltaPercent,
    this.higherIsBetter = true,
    this.trend,
    this.caption,
    this.onTap,
  });

  @override
  State<StatCard> createState() => _StatCardState();
}

class _StatCardState extends State<StatCard> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final accent = widget.tone.foreground(context);

    return MouseRegion(
      cursor: widget.onTap != null ? SystemMouseCursors.click : MouseCursor.defer,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        onTap: widget.onTap,
        child: AnimatedContainer(
          duration: AppDuration.fast,
          curve: AppDuration.curve,
          padding: const EdgeInsets.all(AppSpacing.md + 2),
          decoration: BoxDecoration(
            color: c.surface,
            borderRadius: AppRadius.all(AppRadius.lg),
            border: Border.all(color: _hovered && widget.onTap != null ? c.borderStrong : c.border),
            boxShadow: _hovered && widget.onTap != null ? c.shadowMd : c.shadowSm,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                children: [
                  Container(
                    height: 34,
                    width: 34,
                    decoration: BoxDecoration(
                      color: widget.tone.background(context),
                      borderRadius: AppRadius.all(AppRadius.sm),
                    ),
                    child: Icon(widget.icon, size: 17, color: accent),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Text(
                      widget.label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: context.text.bodySmall?.copyWith(color: c.textSecondary, fontSize: 12.5),
                    ),
                  ),
                  if (widget.deltaPercent != null)
                    DeltaBadge(percent: widget.deltaPercent!, higherIsBetter: widget.higherIsBetter),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              Text(
                widget.value,
                style: context.text.headlineLarge?.copyWith(
                  fontSize: 30,
                  fontFeatures: AppTypography.tabular,
                ),
              ),
              if (widget.caption != null)
                Padding(
                  padding: const EdgeInsets.only(top: 2),
                  child: Text(
                    widget.caption!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12),
                  ),
                ),
              if (widget.trend != null && widget.trend!.length > 1) ...[
                const SizedBox(height: AppSpacing.xs),
                AppSparkline(values: widget.trend!, color: accent, height: 34),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
