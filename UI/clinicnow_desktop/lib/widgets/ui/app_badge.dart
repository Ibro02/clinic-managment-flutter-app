import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';

/// A semantic tone, not a colour. Callers say what a thing *means*
/// ("this appointment was cancelled") and the token layer decides how that
/// looks in the current theme. This is what keeps every status pill in the app
/// identical without any screen importing a hex value.
enum AppTone { neutral, primary, info, success, warning, danger }

extension AppToneX on AppTone {
  Color foreground(BuildContext context) {
    final c = context.colors;
    return switch (this) {
      AppTone.neutral => c.neutral,
      AppTone.primary => c.primary,
      AppTone.info => c.info,
      AppTone.success => c.success,
      AppTone.warning => c.warning,
      AppTone.danger => c.danger,
    };
  }

  Color background(BuildContext context) {
    final c = context.colors;
    return switch (this) {
      AppTone.neutral => c.neutralSoft,
      AppTone.primary => c.primarySoft,
      AppTone.info => c.infoSoft,
      AppTone.success => c.successSoft,
      AppTone.warning => c.warningSoft,
      AppTone.danger => c.dangerSoft,
    };
  }
}

/// A status pill: coloured dot, label, soft background.
///
/// The dot is not decoration. At a glance across forty rows, colour alone is
/// hard to read against a white background and impossible for a colour-blind
/// user; the dot gives the colour a shape to attach to, and the label carries
/// the actual meaning.
class AppStatusBadge extends StatelessWidget {
  final String label;
  final AppTone tone;
  final IconData? icon;
  final bool showDot;

  const AppStatusBadge({
    super.key,
    required this.label,
    this.tone = AppTone.neutral,
    this.icon,
    this.showDot = true,
  });

  @override
  Widget build(BuildContext context) {
    final fg = tone.foreground(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs, vertical: 5),
      decoration: BoxDecoration(
        color: tone.background(context),
        borderRadius: AppRadius.all(AppRadius.xs),
        border: Border.all(color: fg.withValues(alpha: 0.18)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null)
            Padding(
              padding: const EdgeInsets.only(right: 5),
              child: Icon(icon, size: 13, color: fg),
            )
          else if (showDot)
            Container(
              width: 6,
              height: 6,
              margin: const EdgeInsets.only(right: 6),
              decoration: BoxDecoration(color: fg, shape: BoxShape.circle),
            ),
          // One line, always. The pill is used inside table rows of a fixed
          // AppSizes.tableRowHeight, so a long status ("Povrat nije uspio")
          // wrapping to two lines made the badge taller than its row and
          // overflowed it. Flexible lets the label give way to the cell's
          // width instead of pushing past it.
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: context.text.labelMedium?.copyWith(color: fg, fontSize: 12, height: 1.1),
            ),
          ),
        ],
      ),
    );
  }
}

/// A small count next to a heading ("Pacijenti · 248").
class AppCountPill extends StatelessWidget {
  final int count;
  final AppTone tone;

  const AppCountPill({super.key, required this.count, this.tone = AppTone.neutral});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
      decoration: BoxDecoration(
        color: tone.background(context),
        borderRadius: AppRadius.all(AppRadius.xs),
      ),
      child: Text(
        '$count',
        style: context.text.labelMedium?.copyWith(
          color: tone.foreground(context),
          fontSize: 11.5,
          height: 1.2,
          fontFeatures: AppTypography.tabular,
        ),
      ),
    );
  }
}

/// Initials avatar. Colour is derived from the name so the same person keeps
/// the same colour everywhere without storing anything.
class AppAvatar extends StatelessWidget {
  final String name;
  final double size;
  final Color? color;

  const AppAvatar({super.key, required this.name, this.size = 34, this.color});

  static String initials(String value) {
    final parts = value.trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) return '?';
    if (parts.length == 1) return parts.first.substring(0, 1).toUpperCase();
    return (parts.first.substring(0, 1) + parts.last.substring(0, 1)).toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final resolved = color ?? c.series(name.hashCode.abs());

    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: resolved.withValues(alpha: context.isDarkMode ? 0.22 : 0.12),
        borderRadius: AppRadius.all(size / 3.2),
        border: Border.all(color: resolved.withValues(alpha: 0.22)),
      ),
      child: Text(
        initials(name),
        style: context.text.labelMedium?.copyWith(
          color: resolved,
          fontSize: size * 0.36,
          height: 1.1,
        ),
      ),
    );
  }
}
