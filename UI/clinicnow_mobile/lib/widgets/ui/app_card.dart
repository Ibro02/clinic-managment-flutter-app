import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';

/// The single panel primitive. Every boxed area in the app is one of these, so
/// radius, border and shadow can never drift between screens.
class AppCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final String? title;
  final String? subtitle;
  final IconData? icon;
  final List<Widget> actions;
  final bool dense;

  /// Removes internal padding - for cards whose child manages its own edges,
  /// such as a table that needs its header row flush with the border.
  final bool flush;

  /// Makes the whole card a tap target. Null leaves it inert, which is the
  /// right default - a card that lights up under the cursor and then does
  /// nothing is worse than one that never reacted at all.
  final VoidCallback? onTap;

  const AppCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(AppSpacing.lg),
    this.title,
    this.subtitle,
    this.icon,
    this.actions = const [],
    this.dense = false,
    this.flush = false,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final hasHeader = title != null || actions.isNotEmpty;

    return DecoratedBox(
      decoration: BoxDecoration(
        color: c.surface,
        borderRadius: AppRadius.all(AppRadius.lg),
        border: Border.all(color: c.border),
        boxShadow: c.shadowSm,
      ),
      child: ClipRRect(
        borderRadius: AppRadius.all(AppRadius.lg),
        child: _maybeTappable(
          Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (hasHeader) _header(context),
              if (flush)
                child
              else
                Padding(
                  padding: hasHeader
                      ? padding.subtract(EdgeInsets.only(top: dense ? 0 : AppSpacing.xs))
                      : padding,
                  child: child,
                ),
            ],
          ),
        ),
      ),
    );
  }

  /// Adds an ink surface only when there is something to tap, so a plain card
  /// costs no extra Material layer.
  Widget _maybeTappable(Widget content) {
    if (onTap == null) return content;

    return Material(
      color: Colors.transparent,
      child: InkWell(onTap: onTap, child: content),
    );
  }

  Widget _header(BuildContext context) {
    final c = context.colors;

    return Padding(
      padding: EdgeInsets.fromLTRB(
        AppSpacing.lg,
        dense ? AppSpacing.md : AppSpacing.lg,
        AppSpacing.md,
        subtitle == null ? AppSpacing.sm : AppSpacing.md,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          if (icon != null) ...[
            Container(
              height: 32,
              width: 32,
              decoration: BoxDecoration(
                color: c.primarySoft,
                borderRadius: AppRadius.all(AppRadius.sm),
              ),
              child: Icon(icon, size: 17, color: c.primary),
            ),
            const SizedBox(width: AppSpacing.sm),
          ],
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (title != null) Text(title!, style: context.text.titleLarge),
                if (subtitle != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      subtitle!,
                      style: context.text.bodySmall?.copyWith(color: c.textMuted),
                    ),
                  ),
              ],
            ),
          ),
          if (actions.isNotEmpty) ...[
            const SizedBox(width: AppSpacing.sm),
            Row(mainAxisSize: MainAxisSize.min, children: _spaced(actions)),
          ],
        ],
      ),
    );
  }

  static List<Widget> _spaced(List<Widget> items) {
    final out = <Widget>[];
    for (var i = 0; i < items.length; i++) {
      if (i > 0) out.add(const SizedBox(width: AppSpacing.xs));
      out.add(items[i]);
    }
    return out;
  }
}

/// An eyebrow label above a group of cards. Uppercase, tracked, muted - it
/// separates regions of a long page without adding another box.
class AppSectionHeader extends StatelessWidget {
  final String label;
  final Widget? trailing;

  const AppSectionHeader({super.key, required this.label, this.trailing});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.sm, top: AppSpacing.xs),
      child: Row(
        children: [
          Text(label.toUpperCase(), style: context.text.labelSmall),
          const SizedBox(width: AppSpacing.sm),
          Expanded(child: Divider(color: context.colors.border, height: 1)),
          if (trailing != null) ...[const SizedBox(width: AppSpacing.sm), trailing!],
        ],
      ),
    );
  }
}
