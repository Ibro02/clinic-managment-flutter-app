import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'app_badge.dart';

/// An empty screen is an invitation to act, not an apology. Every empty state
/// names what is missing and offers the one action that fixes it.
class AppEmptyState extends StatelessWidget {
  final IconData icon;
  final String title;
  final String message;
  final Widget? action;
  final bool compact;

  const AppEmptyState({
    super.key,
    this.icon = Icons.inbox_outlined,
    required this.title,
    required this.message,
    this.action,
    this.compact = false,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Center(
      child: Padding(
        padding: EdgeInsets.symmetric(vertical: compact ? AppSpacing.xl : AppSpacing.xxl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              height: 52,
              width: 52,
              decoration: BoxDecoration(
                color: c.surfaceMuted,
                borderRadius: AppRadius.all(AppRadius.lg),
                border: Border.all(color: c.border),
              ),
              child: Icon(icon, size: 24, color: c.textMuted),
            ),
            const SizedBox(height: AppSpacing.md),
            Text(title, style: context.text.titleMedium),
            const SizedBox(height: AppSpacing.xxs),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 340),
              child: Text(
                message,
                textAlign: TextAlign.center,
                style: context.text.bodySmall?.copyWith(color: c.textMuted),
              ),
            ),
            if (action != null) ...[const SizedBox(height: AppSpacing.md), action!],
          ],
        ),
      ),
    );
  }
}

/// Errors explain what happened and how to fix it. They do not apologise and
/// they are never vague.
/// [title] is the outcome in the user's terms - "Dokumenti nisu učitani" beats
/// the generic default, which only stands in where the screen has nothing more
/// specific to say. [message] carries the cause and the next step; build it
/// from `failureCause()` so those read the same everywhere.
class AppErrorState extends StatelessWidget {
  final String message;
  final String title;
  final VoidCallback? onRetry;

  const AppErrorState({
    super.key,
    required this.message,
    this.title = 'Podaci nisu učitani',
    this.onRetry,
  });

  @override
  Widget build(BuildContext context) {
    return AppEmptyState(
      icon: Icons.error_outline_rounded,
      title: title,
      message: message,
      action: onRetry == null
          ? null
          : OutlinedButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh_rounded, size: 17),
              label: const Text('Pokušaj ponovo'),
            ),
    );
  }
}

/// A single shimmering placeholder block.
///
/// A spinner tells a person to wait; a skeleton tells them what is coming and
/// stops the layout jumping when data lands. On a table that matters - the
/// header, the column widths and the row rhythm are already correct.
class AppSkeleton extends StatefulWidget {
  final double? width;
  final double height;
  final double radius;

  const AppSkeleton({super.key, this.width, this.height = 12, this.radius = AppRadius.xs});

  @override
  State<AppSkeleton> createState() => _AppSkeletonState();
}

class _AppSkeletonState extends State<AppSkeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1200),
  )..repeat(reverse: true);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) => Container(
        width: widget.width,
        height: widget.height,
        decoration: BoxDecoration(
          color: Color.lerp(c.surfaceMuted, c.border, _controller.value),
          borderRadius: AppRadius.all(widget.radius),
        ),
      ),
    );
  }
}

/// Inline banner for a non-blocking message inside a form or panel.
class AppNotice extends StatelessWidget {
  final String message;
  final AppTone tone;
  final IconData icon;

  const AppNotice({
    super.key,
    required this.message,
    this.tone = AppTone.info,
    this.icon = Icons.info_outline_rounded,
  });

  @override
  Widget build(BuildContext context) {
    final fg = tone.foreground(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: AppSpacing.sm),
      decoration: BoxDecoration(
        color: tone.background(context),
        borderRadius: AppRadius.all(AppRadius.md),
        border: Border.all(color: fg.withValues(alpha: 0.2)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 17, color: fg),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Text(
              message,
              style: context.text.bodySmall?.copyWith(color: fg, height: 1.4),
            ),
          ),
        ],
      ),
    );
  }
}
