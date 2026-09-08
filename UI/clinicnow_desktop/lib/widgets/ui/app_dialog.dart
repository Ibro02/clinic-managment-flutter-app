import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'app_badge.dart';

/// The single dialog shell. Every pop-up in the app - create, edit, confirm,
/// detail - is built from this, which is what makes them feel like one product
/// instead of seven separately-styled boxes.
///
/// Structure is always the same three bands:
///   header  - icon tile, title, one line of context, close button
///   body    - scrolls independently, so a long form never pushes the actions
///             off-screen and the primary button is always reachable
///   footer  - tinted, actions right-aligned, primary last
///
/// That last point matters on desktop: a form that scrolls the whole dialog
/// forces a user to scroll back down to submit. Pinning the footer removes that.
class AppDialog extends StatelessWidget {
  final String title;
  final String? subtitle;
  final IconData? icon;
  final AppTone tone;
  final Widget child;
  final List<Widget> actions;
  final double width;
  final EdgeInsetsGeometry bodyPadding;
  final bool showClose;

  const AppDialog({
    super.key,
    required this.title,
    required this.child,
    this.subtitle,
    this.icon,
    this.tone = AppTone.primary,
    this.actions = const [],
    this.width = 560,
    this.bodyPadding = const EdgeInsets.all(AppSpacing.lg),
    this.showClose = true,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final maxHeight = MediaQuery.sizeOf(context).height * 0.86;

    return Dialog(
      backgroundColor: Colors.transparent,
      elevation: 0,
      insetPadding: const EdgeInsets.all(AppSpacing.lg),
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: width, maxHeight: maxHeight),
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: c.surface,
            borderRadius: AppRadius.all(AppRadius.xl),
            border: Border.all(color: c.border),
            boxShadow: c.shadowLg,
          ),
          child: ClipRRect(
            borderRadius: AppRadius.all(AppRadius.xl),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _header(context),
                Flexible(
                  child: SingleChildScrollView(
                    padding: bodyPadding,
                    child: child,
                  ),
                ),
                if (actions.isNotEmpty) _footer(context),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _header(BuildContext context) {
    final c = context.colors;

    return Container(
      padding: const EdgeInsets.fromLTRB(AppSpacing.lg, AppSpacing.md + 2, AppSpacing.sm, AppSpacing.md + 2),
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: c.border)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          if (icon != null) ...[
            Container(
              height: 38,
              width: 38,
              decoration: BoxDecoration(
                color: tone.background(context),
                borderRadius: AppRadius.all(AppRadius.md),
                border: Border.all(color: tone.foreground(context).withValues(alpha: 0.18)),
              ),
              child: Icon(icon, size: 19, color: tone.foreground(context)),
            ),
            const SizedBox(width: AppSpacing.sm),
          ],
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(title, style: context.text.titleLarge),
                if (subtitle != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      subtitle!,
                      style: context.text.bodySmall?.copyWith(color: c.textMuted, height: 1.35),
                    ),
                  ),
              ],
            ),
          ),
          if (showClose)
            IconButton(
              iconSize: 18,
              tooltip: 'Zatvori',
              onPressed: () => Navigator.of(context).maybePop(),
              icon: const Icon(Icons.close_rounded),
            ),
        ],
      ),
    );
  }

  Widget _footer(BuildContext context) {
    final c = context.colors;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg, vertical: AppSpacing.md),
      decoration: BoxDecoration(
        color: c.surfaceMuted,
        border: Border(top: BorderSide(color: c.border)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.end,
        children: [
          for (var i = 0; i < actions.length; i++) ...[
            if (i > 0) const SizedBox(width: AppSpacing.xs),
            actions[i],
          ],
        ],
      ),
    );
  }
}

/// Opens a dialog with the app's barrier treatment and a short scale-in.
///
/// The motion is 140ms and barely visible on purpose - it tells the eye where
/// the panel came from without making a person wait for an animation they will
/// see forty times a day.
Future<T?> showAppDialog<T>({
  required BuildContext context,
  required WidgetBuilder builder,
  bool barrierDismissible = true,
}) {
  final c = context.colors;

  return showGeneralDialog<T>(
    context: context,
    barrierDismissible: barrierDismissible,
    barrierLabel: MaterialLocalizations.of(context).modalBarrierDismissLabel,
    barrierColor: c.shadowColor.withValues(alpha: context.isDarkMode ? 0.62 : 0.42),
    transitionDuration: const Duration(milliseconds: 140),
    pageBuilder: (context, _, _) => builder(context),
    transitionBuilder: (context, animation, _, child) {
      final curved = CurvedAnimation(parent: animation, curve: Curves.easeOutCubic);
      return FadeTransition(
        opacity: curved,
        child: ScaleTransition(
          scale: Tween<double>(begin: 0.98, end: 1).animate(curved),
          child: child,
        ),
      );
    },
  );
}

/// Destructive and neutral confirmations, so no screen writes its own.
///
/// The confirm label is the verb, never "OK": the button that says "Obriši"
/// produces a result the person predicted before clicking.
Future<bool> showConfirmDialog({
  required BuildContext context,
  required String title,
  required String message,
  String confirmLabel = 'Potvrdi',
  String cancelLabel = 'Odustani',
  bool destructive = false,
  IconData? icon,
}) async {
  final result = await showAppDialog<bool>(
    context: context,
    builder: (context) {
      final c = context.colors;
      final tone = destructive ? AppTone.danger : AppTone.primary;

      return AppDialog(
        title: title,
        icon: icon ?? (destructive ? Icons.delete_outline_rounded : Icons.help_outline_rounded),
        tone: tone,
        width: 460,
        showClose: false,
        actions: [
          OutlinedButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(cancelLabel),
          ),
          FilledButton(
            style: destructive
                ? FilledButton.styleFrom(backgroundColor: c.danger, foregroundColor: Colors.white)
                : null,
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(confirmLabel),
          ),
        ],
        child: Text(
          message,
          style: context.text.bodyMedium?.copyWith(color: c.textSecondary, height: 1.5),
        ),
      );
    },
  );

  return result ?? false;
}

/// ---------------------------------------------------------------------------
/// Form layout
/// ---------------------------------------------------------------------------

/// A labelled group of fields inside a dialog or page.
class AppFormSection extends StatelessWidget {
  final String? label;
  final List<Widget> children;

  const AppFormSection({super.key, this.label, required this.children});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (label != null)
          Padding(
            padding: const EdgeInsets.only(bottom: AppSpacing.sm),
            child: Text(label!.toUpperCase(), style: context.text.labelSmall),
          ),
        for (var i = 0; i < children.length; i++) ...[
          if (i > 0) const SizedBox(height: AppSpacing.md),
          children[i],
        ],
      ],
    );
  }
}

/// Label above field, help text below. Every form control in the app uses this
/// wrapper so label weight, gap and error position never vary.
class AppField extends StatelessWidget {
  final String label;
  final Widget child;
  final String? help;
  final bool required;

  const AppField({
    super.key,
    required this.label,
    required this.child,
    this.help,
    this.required = false,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(label, style: context.text.titleSmall),
            if (required)
              Padding(
                padding: const EdgeInsets.only(left: 3),
                child: Text('*', style: context.text.titleSmall?.copyWith(color: c.danger)),
              ),
          ],
        ),
        const SizedBox(height: 6),
        child,
        if (help != null)
          Padding(
            padding: const EdgeInsets.only(top: 5),
            child: Text(help!, style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12)),
          ),
      ],
    );
  }
}

/// Two fields side by side, stacking below 520px so a narrow window never
/// squeezes a date picker into forty pixels.
class AppFieldRow extends StatelessWidget {
  final List<Widget> children;

  const AppFieldRow({super.key, required this.children});

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < 520) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (var i = 0; i < children.length; i++) ...[
                if (i > 0) const SizedBox(height: AppSpacing.md),
                children[i],
              ],
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var i = 0; i < children.length; i++) ...[
              if (i > 0) const SizedBox(width: AppSpacing.md),
              Expanded(child: children[i]),
            ],
          ],
        );
      },
    );
  }
}
