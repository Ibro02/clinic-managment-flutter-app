import 'package:flutter/material.dart';

import '../../core/design_tokens.dart';
import 'app_states.dart';

/// One column of an [AppDataTable].
///
/// Width is either fixed (`width`) or proportional (`flex`). Fixed is right for
/// anything whose content has a known maximum - a date, a status, an action
/// column - because a proportional date column wobbles as the window resizes,
/// which is the single most obvious tell of a hand-rolled grid.
class AppColumn<T> {
  final String label;
  final Widget Function(BuildContext context, T row) cell;
  final double? width;
  final int flex;

  /// Right-aligns and applies tabular figures. Use for money, counts, durations.
  final bool numeric;

  /// Non-null makes the header clickable and reports this key back via `onSort`.
  final String? sortKey;

  const AppColumn({
    required this.label,
    required this.cell,
    this.width,
    this.flex = 1,
    this.numeric = false,
    this.sortKey,
  });
}

/// Paging state supplied by the caller. The table renders the footer and calls
/// back; it never fetches anything itself.
class AppTablePaging {
  final int page; // zero-based
  final int pageSize;
  final int totalCount;

  const AppTablePaging({required this.page, required this.pageSize, required this.totalCount});

  int get totalPages => totalCount <= 0 ? 1 : ((totalCount + pageSize - 1) ~/ pageSize);
  int get firstItem => totalCount == 0 ? 0 : page * pageSize + 1;
  int get lastItem => ((page + 1) * pageSize).clamp(0, totalCount);
  bool get hasPrevious => page > 0;
  bool get hasNext => page + 1 < totalPages;
}

/// The one grid used everywhere in the app.
///
/// Consistency here is the whole point: patients, doctors, appointments and all
/// five codebooks render through this widget, so row height, header treatment,
/// hover feedback, sorting affordance, empty copy and pagination are identical
/// by construction rather than by discipline.
///
/// Design notes worth keeping:
///   * The header is a plain surface with a bottom hairline, not a grey block.
///     A grey header band visually competes with the data; a rule does the same
///     job with less ink.
///   * Row actions are hidden until the row is hovered. Forty rows of visible
///     icon buttons is forty times the visual noise for one action a user takes
///     once a minute.
///   * Numeric cells use tabular figures so digits align down the column.
class AppDataTable<T> extends StatelessWidget {
  final List<AppColumn<T>> columns;
  final List<T> rows;

  final bool isLoading;
  final String? error;
  final VoidCallback? onRetry;

  final void Function(T row)? onRowTap;

  /// Rendered in a trailing fixed-width column, revealed on row hover.
  final List<Widget> Function(BuildContext context, T row)? rowActions;
  final double actionsWidth;

  final String? sortKey;
  final bool sortAscending;
  final void Function(String key)? onSort;

  final AppTablePaging? paging;
  final ValueChanged<int>? onPageChanged;

  final String emptyTitle;
  final String emptyMessage;
  final Widget? emptyAction;

  /// True when the table should fill the remaining height of a bounded parent
  /// (the usual case for a full-page grid). False when it sits inside a
  /// scrolling column and should size to its content.
  final bool expand;

  final double rowHeight;

  const AppDataTable({
    super.key,
    required this.columns,
    required this.rows,
    this.isLoading = false,
    this.error,
    this.onRetry,
    this.onRowTap,
    this.rowActions,
    this.actionsWidth = 104,
    this.sortKey,
    this.sortAscending = true,
    this.onSort,
    this.paging,
    this.onPageChanged,
    this.emptyTitle = 'Nema zapisa',
    this.emptyMessage = 'Kada dodate prvi zapis, pojavit će se ovdje.',
    this.emptyAction,
    this.expand = true,
    this.rowHeight = AppSizes.tableRowHeight,
  });

  /// The actions column's real width, computed from how many [AppRowAction]s
  /// the current rows actually render rather than a per-screen guessed
  /// constant. Every [AppRowAction] is a fixed [AppRowAction.width] regardless
  /// of icon, so N actions always need exactly N × that width - a flat
  /// default (previously 104, i.e. room for 3) silently overflowed as soon as
  /// any screen's `extraRowActions` pushed a row past whatever count the
  /// default happened to fit, on every grid that used it, not just one.
  /// [actionsWidth] remains the fallback for the loading skeleton and the
  /// empty state, where there are no real rows yet to measure.
  double _resolveActionsWidth(BuildContext context) {
    if (rowActions == null) return 0;
    if (rows.isEmpty) return actionsWidth;

    var maxActions = 0;
    for (final row in rows) {
      final count = rowActions!(context, row).length;
      if (count > maxActions) maxActions = count;
    }
    return maxActions * AppRowAction.width;
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final resolvedActionsWidth = _resolveActionsWidth(context);

    final Widget body;
    if (error != null) {
      body = AppErrorState(message: error!, onRetry: onRetry);
    } else if (isLoading) {
      body = _skeleton(context);
    } else if (rows.isEmpty) {
      body = AppEmptyState(
        icon: Icons.table_rows_outlined,
        title: emptyTitle,
        message: emptyMessage,
        action: emptyAction,
      );
    } else {
      body = ListView.builder(
        padding: EdgeInsets.zero,
        shrinkWrap: !expand,
        physics: expand ? null : const NeverScrollableScrollPhysics(),
        itemCount: rows.length,
        itemBuilder: (context, index) => _TableRow<T>(
          row: rows[index],
          columns: columns,
          rowActions: rowActions,
          actionsWidth: resolvedActionsWidth,
          onTap: onRowTap,
          height: rowHeight,
          isLast: index == rows.length - 1,
        ),
      );
    }

    return DecoratedBox(
      decoration: BoxDecoration(
        color: c.surface,
        borderRadius: AppRadius.all(AppRadius.lg),
        border: Border.all(color: c.border),
        boxShadow: c.shadowSm,
      ),
      child: ClipRRect(
        borderRadius: AppRadius.all(AppRadius.lg),
        child: Column(
          mainAxisSize: expand ? MainAxisSize.max : MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _header(context, resolvedActionsWidth),
            if (expand) Expanded(child: body) else Flexible(child: body),
            if (paging != null) _footer(context, paging!),
          ],
        ),
      ),
    );
  }

  // --- header ---------------------------------------------------------------

  Widget _header(BuildContext context, double resolvedActionsWidth) {
    final c = context.colors;

    return Container(
      height: AppSizes.tableHeaderHeight,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: c.surface,
        border: Border(bottom: BorderSide(color: c.border)),
      ),
      child: Row(
        children: [
          for (final column in columns)
            _sized(
              column,
              _HeaderCell(
                column: column,
                active: sortKey != null && sortKey == column.sortKey,
                ascending: sortAscending,
                onSort: onSort,
              ),
            ),
          if (rowActions != null) SizedBox(width: resolvedActionsWidth),
        ],
      ),
    );
  }

  Widget _sized(AppColumn<T> column, Widget child) {
    if (column.width != null) return SizedBox(width: column.width, child: child);
    return Expanded(flex: column.flex, child: child);
  }

  // --- loading --------------------------------------------------------------

  /// Skeleton rows mirror the real column widths so nothing shifts when the
  /// data arrives.
  Widget _skeleton(BuildContext context) {
    final c = context.colors;
    const rowCount = 7;

    return ListView.builder(
      padding: EdgeInsets.zero,
      shrinkWrap: !expand,
      physics: const NeverScrollableScrollPhysics(),
      itemCount: rowCount,
      itemBuilder: (context, index) => Container(
        height: rowHeight,
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
        decoration: BoxDecoration(
          border: Border(bottom: BorderSide(color: index == rowCount - 1 ? Colors.transparent : c.border)),
        ),
        child: Row(
          children: [
            for (var i = 0; i < columns.length; i++)
              _sized(
                columns[i],
                Align(
                  alignment: columns[i].numeric ? Alignment.centerRight : Alignment.centerLeft,
                  child: Padding(
                    padding: const EdgeInsets.only(right: AppSpacing.md),
                    child: AppSkeleton(width: i == 0 ? 150 : 78, height: 11),
                  ),
                ),
              ),
            if (rowActions != null) SizedBox(width: actionsWidth),
          ],
        ),
      ),
    );
  }

  // --- footer ---------------------------------------------------------------

  Widget _footer(BuildContext context, AppTablePaging p) {
    final c = context.colors;

    return Container(
      height: 52,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: c.surfaceMuted,
        border: Border(top: BorderSide(color: c.border)),
      ),
      child: Row(
        children: [
          Text(
            p.totalCount == 0
                ? 'Nema rezultata'
                : 'Prikazano ${p.firstItem}–${p.lastItem} od ${p.totalCount}',
            style: context.text.bodySmall?.copyWith(
              color: c.textMuted,
              fontFeatures: AppTypography.tabular,
            ),
          ),
          const Spacer(),
          Text(
            'Stranica ${p.page + 1} / ${p.totalPages}',
            style: context.text.labelMedium?.copyWith(
              color: c.textSecondary,
              fontFeatures: AppTypography.tabular,
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          _pagerButton(
            context,
            icon: Icons.chevron_left_rounded,
            tooltip: 'Prethodna',
            enabled: p.hasPrevious,
            onTap: () => onPageChanged?.call(p.page - 1),
          ),
          const SizedBox(width: AppSpacing.xxs),
          _pagerButton(
            context,
            icon: Icons.chevron_right_rounded,
            tooltip: 'Sljedeća',
            enabled: p.hasNext,
            onTap: () => onPageChanged?.call(p.page + 1),
          ),
        ],
      ),
    );
  }

  Widget _pagerButton(
    BuildContext context, {
    required IconData icon,
    required String tooltip,
    required bool enabled,
    required VoidCallback onTap,
  }) {
    final c = context.colors;

    return Tooltip(
      message: tooltip,
      child: Material(
        color: enabled ? c.surface : Colors.transparent,
        borderRadius: AppRadius.all(AppRadius.sm),
        child: InkWell(
          borderRadius: AppRadius.all(AppRadius.sm),
          onTap: enabled ? onTap : null,
          child: Container(
            height: 30,
            width: 30,
            decoration: BoxDecoration(
              borderRadius: AppRadius.all(AppRadius.sm),
              border: Border.all(color: enabled ? c.border : Colors.transparent),
            ),
            child: Icon(icon, size: 18, color: enabled ? c.textSecondary : c.textMuted.withValues(alpha: 0.4)),
          ),
        ),
      ),
    );
  }
}

/// ---------------------------------------------------------------------------

class _HeaderCell<T> extends StatefulWidget {
  final AppColumn<T> column;
  final bool active;
  final bool ascending;
  final void Function(String key)? onSort;

  const _HeaderCell({
    required this.column,
    required this.active,
    required this.ascending,
    required this.onSort,
  });

  @override
  State<_HeaderCell<T>> createState() => _HeaderCellState<T>();
}

class _HeaderCellState<T> extends State<_HeaderCell<T>> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final column = widget.column;
    final sortable = column.sortKey != null && widget.onSort != null;

    final label = Text(
      column.label.toUpperCase(),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: context.text.labelSmall?.copyWith(
        color: widget.active ? c.textPrimary : c.textMuted,
        fontSize: 10.5,
      ),
    );

    // The arrow appears on hover before a column is sorted, so the affordance
    // is discoverable without permanently cluttering every header.
    final showArrow = sortable && (widget.active || _hovered);

    final content = Row(
      mainAxisAlignment: column.numeric ? MainAxisAlignment.end : MainAxisAlignment.start,
      children: [
        Flexible(child: label),
        if (showArrow)
          Padding(
            padding: const EdgeInsets.only(left: 3),
            child: Icon(
              widget.active
                  ? (widget.ascending ? Icons.arrow_upward_rounded : Icons.arrow_downward_rounded)
                  : Icons.unfold_more_rounded,
              size: 13,
              color: widget.active ? c.primary : c.textMuted,
            ),
          ),
      ],
    );

    if (!sortable) {
      return Padding(
        padding: const EdgeInsets.only(right: AppSpacing.md),
        child: Align(
          alignment: column.numeric ? Alignment.centerRight : Alignment.centerLeft,
          child: content,
        ),
      );
    }

    return MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: () => widget.onSort!(column.sortKey!),
        child: Padding(
          padding: const EdgeInsets.only(right: AppSpacing.md),
          child: Align(
            alignment: column.numeric ? Alignment.centerRight : Alignment.centerLeft,
            child: content,
          ),
        ),
      ),
    );
  }
}

/// ---------------------------------------------------------------------------

class _TableRow<T> extends StatefulWidget {
  final T row;
  final List<AppColumn<T>> columns;
  final List<Widget> Function(BuildContext context, T row)? rowActions;
  final double actionsWidth;
  final void Function(T row)? onTap;
  final double height;
  final bool isLast;

  const _TableRow({
    required this.row,
    required this.columns,
    required this.rowActions,
    required this.actionsWidth,
    required this.onTap,
    required this.height,
    required this.isLast,
  });

  @override
  State<_TableRow<T>> createState() => _TableRowState<T>();
}

class _TableRowState<T> extends State<_TableRow<T>> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    final cells = <Widget>[
      for (final column in widget.columns)
        if (column.width != null)
          SizedBox(width: column.width, child: _cell(context, column))
        else
          Expanded(flex: column.flex, child: _cell(context, column)),
    ];

    return MouseRegion(
      cursor: widget.onTap != null ? SystemMouseCursors.click : MouseCursor.defer,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onTap == null ? null : () => widget.onTap!(widget.row),
        child: AnimatedContainer(
          duration: AppDuration.fast,
          height: widget.height,
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
          decoration: BoxDecoration(
            color: _hovered ? c.surfaceHover : c.surface,
            border: Border(
              bottom: BorderSide(color: widget.isLast ? Colors.transparent : c.border),
            ),
          ),
          child: Row(
            children: [
              ...cells,
              if (widget.rowActions != null)
                SizedBox(
                  width: widget.actionsWidth,
                  child: AnimatedOpacity(
                    duration: AppDuration.fast,
                    opacity: _hovered ? 1 : 0,
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: widget.rowActions!(context, widget.row),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _cell(BuildContext context, AppColumn<T> column) {
    final child = DefaultTextStyle.merge(
      style: column.numeric
          ? context.text.bodyMedium!.copyWith(fontFeatures: AppTypography.tabular)
          : context.text.bodyMedium!,
      overflow: TextOverflow.ellipsis,
      maxLines: 1,
      child: column.cell(context, widget.row),
    );

    return Padding(
      padding: const EdgeInsets.only(right: AppSpacing.md),
      child: Align(
        alignment: column.numeric ? Alignment.centerRight : Alignment.centerLeft,
        child: child,
      ),
    );
  }
}

/// A compact icon button sized for a table row. Kept here so every grid's
/// actions look the same.
class AppRowAction extends StatelessWidget {
  /// Rendered width of one action (matches the tight [BoxConstraints] below).
  /// [AppDataTable] reads this to size its actions column from the actual
  /// number of actions a row renders, instead of a flat per-screen guess.
  static const double width = 32.0;

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final bool destructive;

  const AppRowAction({
    super.key,
    required this.icon,
    required this.tooltip,
    this.onPressed,
    this.destructive = false,
  });

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Tooltip(
      message: tooltip,
      child: IconButton(
        onPressed: onPressed,
        iconSize: 17,
        constraints: const BoxConstraints.tightFor(width: 32, height: 32),
        padding: EdgeInsets.zero,
        style: IconButton.styleFrom(
          foregroundColor: destructive ? c.danger : c.textSecondary,
          hoverColor: destructive ? c.dangerSoft : c.surfaceMuted,
          shape: RoundedRectangleBorder(borderRadius: AppRadius.all(AppRadius.sm)),
        ),
        icon: Icon(icon),
      ),
    );
  }
}
