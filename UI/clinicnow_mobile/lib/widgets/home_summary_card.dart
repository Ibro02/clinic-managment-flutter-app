import 'package:flutter/material.dart';

import '../core/design_tokens.dart';
import 'ui/app_badge.dart';
import 'ui/app_card.dart';

/// Greeting + at-a-glance appointment counters, shown at the very top of the
/// patient's home screen above the news feed.
///
/// Deliberately presentational: the counts are fetched by the home screen
/// itself, which already owns a pull-to-refresh, so there is exactly one place
/// that decides when this data is stale instead of two loaders racing on the
/// same screen.
class HomeSummaryCard extends StatelessWidget {
  /// The patient's own name, from the JWT session. Empty is tolerated (the
  /// card falls back to a nameless greeting) so a profile with no first name
  /// never renders "Dobar dan, !".
  final String name;

  /// Null while loading, or when the count request failed - the card shows a
  /// placeholder rather than a wrong "0", which a patient would read as
  /// "my appointment is gone".
  final int? pendingCount;
  final int? confirmedCount;

  const HomeSummaryCard({
    super.key,
    required this.name,
    this.pendingCount,
    this.confirmedCount,
  });

  /// Time-of-day greeting in the patient's local time.
  static String greetingFor(DateTime localNow) {
    final hour = localNow.hour;
    if (hour < 5) return 'Dobra večer';
    if (hour < 12) return 'Dobro jutro';
    if (hour < 18) return 'Dobar dan';
    return 'Dobra večer';
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final trimmedName = name.trim();
    final greeting = greetingFor(DateTime.now());

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              AppAvatar(name: trimmedName.isEmpty ? '?' : trimmedName, size: 44),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      trimmedName.isEmpty ? 'Dobrodošli' : greeting,
                      style: context.text.bodySmall?.copyWith(color: c.textSecondary),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      trimmedName.isEmpty ? 'u ClinicNow' : trimmedName,
                      style: context.text.titleMedium,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          // No `CrossAxisAlignment.stretch` here: the card lives inside the
          // home screen's scrolling list, where height is unbounded, and
          // stretch would ask these tiles to be infinitely tall.
          Row(
            children: [
              Expanded(
                child: _CountTile(
                  icon: Icons.hourglass_bottom_outlined,
                  label: 'Na čekanju',
                  count: pendingCount,
                  tone: AppTone.warning,
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: _CountTile(
                  icon: Icons.event_available_outlined,
                  label: 'Potvrđeni',
                  count: confirmedCount,
                  tone: AppTone.info,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// One counter. Tones match the status pills on "Moji termini" (pending =
/// warning, confirmed = info) so the same status never changes colour between
/// the home screen and the list it summarises.
class _CountTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final int? count;
  final AppTone tone;

  const _CountTile({
    required this.icon,
    required this.label,
    required this.count,
    required this.tone,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final fg = tone.foreground(context);

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.sm - 2,
      ),
      decoration: BoxDecoration(
        color: tone.background(context),
        borderRadius: AppRadius.all(AppRadius.md),
        border: Border.all(color: fg.withValues(alpha: 0.18)),
      ),
      child: Row(
        children: [
          Icon(icon, size: 20, color: fg),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  count?.toString() ?? '—',
                  style: context.text.titleLarge?.copyWith(
                    color: fg,
                    height: 1.1,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  label,
                  style: context.text.bodySmall?.copyWith(color: c.textSecondary),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
