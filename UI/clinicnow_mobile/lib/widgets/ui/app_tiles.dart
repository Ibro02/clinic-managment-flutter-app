import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'app_badge.dart';

/// ---------------------------------------------------------------------------
/// Mobile-native list and picker primitives
/// ---------------------------------------------------------------------------
/// The desktop app scans data in a grid; the phone reads it as a stack of
/// cards. These are the mobile counterparts to `AppDataTable` - same tokens,
/// same status vocabulary, different shape - so an appointment reads as the
/// same object in both apps without the phone pretending to be a spreadsheet.
/// ---------------------------------------------------------------------------

/// One row of any list on the phone: an icon tile, two lines of text, an
/// optional status badge, and a chevron when it opens something.
///
/// A card rather than a `ListTile` because a card gives the row an edge. On a
/// white background a list of ListTiles is an undifferentiated column of text;
/// bounded cards let the eye count the items without reading them.
class AppListCard extends StatelessWidget {
  final IconData icon;
  final AppTone tone;
  final String title;
  final String subtitle;

  /// Optional third line - the quiet one, for metadata rather than content.
  final String? meta;

  /// Status pill on the right.
  final String? badgeLabel;
  final AppTone? badgeTone;

  final VoidCallback? onTap;
  final List<Widget> actions;

  /// Draws a filled dot in the corner. Used for unread notifications.
  final bool highlighted;

  /// How many lines the subtitle may occupy. Two keeps a list scannable and is
  /// right for a one-sentence subtitle; a lab finding needs more, since its
  /// measurement, interpretation and the doctor's remark are separate lines.
  final int subtitleMaxLines;

  const AppListCard({
    super.key,
    required this.icon,
    required this.title,
    required this.subtitle,
    this.tone = AppTone.primary,
    this.meta,
    this.badgeLabel,
    this.badgeTone,
    this.onTap,
    this.actions = const [],
    this.highlighted = false,
    this.subtitleMaxLines = 2,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Material(
      color: highlighted ? c.primarySoft : c.surface,
      borderRadius: AppRadius.all(AppRadius.lg),
      child: InkWell(
        onTap: onTap,
        borderRadius: AppRadius.all(AppRadius.lg),
        child: Container(
          padding: const EdgeInsets.all(AppSpacing.sm + 2),
          decoration: BoxDecoration(
            borderRadius: AppRadius.all(AppRadius.lg),
            border: Border.all(color: highlighted ? c.primary.withValues(alpha: 0.28) : c.border),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 42,
                height: 42,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tone.background(context),
                  borderRadius: AppRadius.all(AppRadius.md),
                ),
                child: Icon(icon, size: 20, color: tone.foreground(context)),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      title,
                      style: context.text.titleSmall,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 3),
                    Text(
                      subtitle,
                      style: context.text.bodySmall?.copyWith(color: c.textSecondary),
                      maxLines: subtitleMaxLines,
                      overflow: TextOverflow.ellipsis,
                    ),
                    if (meta != null) ...[
                      const SizedBox(height: 3),
                      Text(
                        meta!,
                        style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                    if (badgeLabel != null) ...[
                      const SizedBox(height: AppSpacing.xs),
                      AppStatusBadge(label: badgeLabel!, tone: badgeTone ?? tone),
                    ],
                  ],
                ),
              ),
              if (actions.isNotEmpty) ...[
                const SizedBox(width: AppSpacing.xxs),
                ...actions,
              ] else if (onTap != null)
                Padding(
                  padding: const EdgeInsets.only(top: AppSpacing.sm),
                  child: Icon(Icons.chevron_right_rounded, size: 20, color: c.textMuted),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The greeting block at the top of a patient-facing screen.
///
/// Mobile can afford this where the desktop cannot: one large line that says
/// who is looking and what this screen is for, before any data. It is the
/// single place in the phone app where type is allowed to be large.
class AppGreetingHeader extends StatelessWidget {
  final String greeting;
  final String subtitle;
  final Widget? trailing;

  const AppGreetingHeader({
    super.key,
    required this.greeting,
    required this.subtitle,
    this.trailing,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.md),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(greeting, style: context.text.headlineSmall),
                const SizedBox(height: 2),
                Text(subtitle, style: context.text.bodySmall?.copyWith(color: c.textMuted)),
              ],
            ),
          ),
          ?trailing,
        ],
      ),
    );
  }
}

/// Time-slot picker: a wrap of selectable times.
///
/// Slots are the moment a booking flow either feels effortless or does not, so
/// they get real touch targets rather than Material's default dense chips - a
/// mistyped tap here books the wrong hour.
class AppSlotPicker extends StatelessWidget {
  final List<DateTime> slots;
  final DateTime? selected;
  final ValueChanged<DateTime> onSelected;
  final String Function(DateTime slot) labelBuilder;

  const AppSlotPicker({
    super.key,
    required this.slots,
    required this.selected,
    required this.onSelected,
    required this.labelBuilder,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: [
        for (final slot in slots)
          _Slot(
            label: labelBuilder(slot),
            selected: selected == slot,
            onTap: () => onSelected(slot),
            colors: c,
          ),
      ],
    );
  }
}

class _Slot extends StatelessWidget {
  final String label;
  final bool selected;
  final VoidCallback onTap;
  final AppColors colors;

  const _Slot({
    required this.label,
    required this.selected,
    required this.onTap,
    required this.colors,
  });

  @override
  Widget build(BuildContext context) {
    return Material(
      color: selected ? colors.primary : colors.surface,
      borderRadius: AppRadius.all(AppRadius.md),
      child: InkWell(
        onTap: onTap,
        borderRadius: AppRadius.all(AppRadius.md),
        child: AnimatedContainer(
          duration: AppDuration.fast,
          curve: AppDuration.curve,
          height: 44,
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
          alignment: Alignment.center,
          decoration: BoxDecoration(
            borderRadius: AppRadius.all(AppRadius.md),
            border: Border.all(color: selected ? colors.primary : colors.border),
          ),
          child: Text(
            label,
            style: context.text.labelLarge?.copyWith(
              color: selected ? colors.onPrimary : colors.textPrimary,
              fontFeatures: AppTypography.tabular,
            ),
          ),
        ),
      ),
    );
  }
}
