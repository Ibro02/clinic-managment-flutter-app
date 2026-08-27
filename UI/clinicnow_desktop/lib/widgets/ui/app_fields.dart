import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';

/// The search box that sits above every grid.
class AppSearchField extends StatelessWidget {
  final String hint;
  final ValueChanged<String>? onChanged;
  final VoidCallback? onClear;
  final TextEditingController? controller;
  final double width;

  const AppSearchField({
    super.key,
    this.hint = 'Pretraži…',
    this.onChanged,
    this.onClear,
    this.controller,
    this.width = 280,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return SizedBox(
      width: width,
      height: AppSizes.controlHeight,
      child: TextField(
        controller: controller,
        onChanged: onChanged,
        style: context.text.bodyMedium,
        decoration: InputDecoration(
          hintText: hint,
          prefixIcon: Icon(Icons.search_rounded, size: 18, color: c.textMuted),
          prefixIconConstraints: const BoxConstraints(minWidth: 38, minHeight: 38),
          suffixIcon: onClear == null
              ? null
              : IconButton(
                  iconSize: 16,
                  onPressed: onClear,
                  icon: const Icon(Icons.close_rounded),
                ),
          contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: 0),
        ),
      ),
    );
  }
}

/// Page-level toolbar: title on the left, filters and the primary action on the
/// right. Sits directly above a grid.
class AppToolbar extends StatelessWidget {
  final String? title;
  final String? subtitle;
  final List<Widget> filters;
  final List<Widget> actions;

  const AppToolbar({
    super.key,
    this.title,
    this.subtitle,
    this.filters = const [],
    this.actions = const [],
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.md),
      child: Wrap(
        alignment: WrapAlignment.spaceBetween,
        crossAxisAlignment: WrapCrossAlignment.center,
        runSpacing: AppSpacing.sm,
        spacing: AppSpacing.md,
        children: [
          if (title != null)
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(title!, style: context.text.headlineSmall),
                if (subtitle != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(subtitle!, style: context.text.bodySmall?.copyWith(color: c.textMuted)),
                  ),
              ],
            ),
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final filter in filters) ...[filter, const SizedBox(width: AppSpacing.xs)],
              for (final action in actions) ...[const SizedBox(width: AppSpacing.xxs), action],
            ],
          ),
        ],
      ),
    );
  }
}

/// A compact segmented control - the range picker on chart cards, the filter
/// switch above a grid.
///
/// Preferred over a dropdown when there are two to four options: a dropdown
/// hides the alternatives behind a click, and hiding three words is not worth
/// the interaction.
class AppSegmented<T> extends StatelessWidget {
  final List<(T value, String label)> options;
  final T selected;
  final ValueChanged<T> onChanged;

  const AppSegmented({
    super.key,
    required this.options,
    required this.selected,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Container(
      height: AppSizes.controlHeightSm,
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: c.surfaceMuted,
        borderRadius: AppRadius.all(AppRadius.sm),
        border: Border.all(color: c.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final (value, label) in options)
            _segment(context, value: value, label: label, active: value == selected),
        ],
      ),
    );
  }

  Widget _segment(BuildContext context, {required T value, required String label, required bool active}) {
    final c = context.colors;

    return MouseRegion(
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: () => onChanged(value),
        behavior: HitTestBehavior.opaque,
        child: AnimatedContainer(
          duration: AppDuration.fast,
          curve: AppDuration.curve,
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: active ? c.surface : Colors.transparent,
            borderRadius: AppRadius.all(AppRadius.xs),
            boxShadow: active ? c.shadowSm : null,
            border: Border.all(color: active ? c.border : Colors.transparent),
          ),
          child: Text(
            label,
            style: context.text.labelMedium?.copyWith(
              color: active ? c.textPrimary : c.textMuted,
              fontSize: 12.5,
            ),
          ),
        ),
      ),
    );
  }
}
