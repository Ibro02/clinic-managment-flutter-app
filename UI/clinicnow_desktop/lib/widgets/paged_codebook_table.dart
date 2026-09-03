import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_exception.dart';
import '../core/base_provider.dart';
import '../core/design_tokens.dart';
import '../models/paged_result.dart';
import 'ui/app_data_table.dart';
import 'ui/app_dialog.dart';
import 'ui/app_fields.dart';

/// Reusable "list + search + paging + row actions" shell shared by every
/// codebook screen (City/Specialization/Location/MedicalService) - the table
/// columns and the add/edit forms differ per entity (so those stay in each
/// screen), but the paging/search/delete-confirmation chrome around them is
/// identical, so it lives here once instead of four times (CLAUDE.md - DRY).
///
/// Search is debounced client-side before hitting the API, and paging/
/// filtering/sorting all go through the real `GET api/<Entity>` endpoint
/// (rulebook Part II §D: pagination and filtering happen at the database,
/// never in-memory).
///
/// The chrome is assembled entirely from the shared component library, which is
/// what makes nine screens share one page shape by construction rather than by
/// discipline:
///   [AppToolbar]      - page title, search, primary action
///   [AppDataTable]    - header, rows, loading skeleton, error, empty, paging
///   [showConfirmDialog] - the one destructive-confirmation popup
class PagedCodebookTable<T> extends StatefulWidget {
  final String title;

  /// One line of context under the title. Optional - a codebook whose name
  /// already says everything does not need a sentence explaining it.
  final String? subtitle;

  final String searchHint;
  final BaseProvider<T> provider;

  /// Column set for [AppDataTable]. Each column carries its own cell builder,
  /// so unlike Material's `DataTable` there is no second parallel list of cells
  /// to keep in the same order.
  final List<AppColumn<T>> Function() buildColumns;

  final VoidCallback onAdd;
  final void Function(T item) onEdit;
  final Future<void> Function(T item) onDelete;
  final String Function(T item) itemLabel;

  /// Label for the primary action. Defaults to the generic "Dodaj"; screens
  /// that can name the thing being added ("Dodaj pacijenta") should.
  final String addLabel;

  /// Optional summary cards rendered between the toolbar and the grid - the
  /// KPI row of the dashboard-CRM page shape. Empty on codebooks, where a
  /// count of cities is not worth a card.
  final List<Widget> stats;

  /// Server-side `orderBy` column name. Defaults to `Name`; entities without
  /// that property (e.g. Patient/Doctor, which sort by `LastName`) override it.
  final String orderBy;

  /// Extra fixed query parameters merged into every `getPaged` call (e.g.
  /// `{'doctorId': 3}` to scope a schedule table to one doctor). Re-evaluated
  /// on every [load] call, so a parent can rebuild this widget with a new
  /// filter value.
  final Map<String, dynamic> extraSearchParams;

  /// Hides the "Dodaj" button and the per-row edit/delete actions for roles
  /// that can read but not write this resource (e.g. Doctor viewing patients).
  /// The backend independently enforces the same boundary - this is just UX.
  final bool canWrite;

  /// Additional per-row icon buttons rendered before edit/delete (e.g. "open
  /// schedule" on a Doctor row). Always shown, regardless of [canWrite].
  final List<Widget> Function(T item)? extraRowActions;

  /// Hides the search box entirely for resources with no name-based filter
  /// on the backend (e.g. WorkingHours/ScheduleBlock, scoped by doctor
  /// instead via [extraSearchParams]).
  final bool showSearch;

  /// False when the table sits inside an already-scrolling column (the two
  /// stacked tables on the doctor-schedule screen) rather than filling a page.
  final bool expand;

  const PagedCodebookTable({
    super.key,
    required this.title,
    required this.searchHint,
    required this.provider,
    required this.buildColumns,
    required this.onAdd,
    required this.onEdit,
    required this.onDelete,
    required this.itemLabel,
    this.subtitle,
    this.addLabel = 'Dodaj',
    this.stats = const [],
    this.orderBy = 'Name',
    this.extraSearchParams = const {},
    this.canWrite = true,
    this.extraRowActions,
    this.showSearch = true,
    this.expand = true,
  });

  @override
  State<PagedCodebookTable<T>> createState() => PagedCodebookTableState<T>();
}

class PagedCodebookTableState<T> extends State<PagedCodebookTable<T>> {
  static const int _pageSize = 10;

  final _searchController = TextEditingController();
  Timer? _debounce;

  /// One-based, matching the API's `page` parameter. [AppTablePaging] is
  /// zero-based, so the two are converted at the boundary in [build] rather
  /// than leaking an off-by-one into the request.
  int _page = 1;
  bool _isLoading = true;
  String? _error;
  PagedResult<T>? _result;

  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      _page = 1;
      load();
    });
  }

  Future<void> load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final search = <String, dynamic>{
        'page': _page,
        'pageSize': _pageSize,
        'orderBy': widget.orderBy,
        if (_searchController.text.trim().isNotEmpty) 'name': _searchController.text.trim(),
        ...widget.extraSearchParams,
      };
      final result = await widget.provider.getPaged(search);
      if (!mounted) return;
      setState(() => _result = result);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _confirmDelete(T item) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Potvrda brisanja',
      message:
          'Da li ste sigurni da želite obrisati "${widget.itemLabel(item)}"? '
          'Ova radnja se ne može poništiti.',
      confirmLabel: 'Obriši',
      destructive: true,
    );

    if (!confirmed) return;

    try {
      await widget.onDelete(item);
      await load();
    } on ApiException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  void _goToPage(int zeroBasedPage) {
    setState(() => _page = zeroBasedPage + 1);
    load();
  }

  @override
  Widget build(BuildContext context) {
    final rows = _result?.resultList ?? const [];

    final table = AppDataTable<T>(
      columns: widget.buildColumns(),
      rows: rows,
      isLoading: _isLoading,
      error: _error,
      onRetry: load,
      expand: widget.expand,
      emptyTitle: 'Nema zapisa',
      emptyMessage: _searchController.text.trim().isEmpty
          ? 'Kada dodate prvi zapis, pojavit će se ovdje.'
          : 'Nijedan zapis ne odgovara pretrazi "${_searchController.text.trim()}".',
      emptyAction: widget.canWrite && _searchController.text.trim().isEmpty
          ? FilledButton.icon(
              onPressed: widget.onAdd,
              icon: const Icon(Icons.add_rounded, size: 18),
              label: Text(widget.addLabel),
            )
          : null,
      paging: AppTablePaging(page: _page - 1, pageSize: _pageSize, totalCount: _result?.count ?? 0),
      onPageChanged: _goToPage,
      rowActions: (context, item) => [
        ...?widget.extraRowActions?.call(item),
        if (widget.canWrite) ...[
          AppRowAction(icon: Icons.edit_outlined, tooltip: 'Uredi', onPressed: () => widget.onEdit(item)),
          AppRowAction(
            icon: Icons.delete_outline_rounded,
            tooltip: 'Obriši',
            destructive: true,
            onPressed: () => _confirmDelete(item),
          ),
        ],
      ],
    );

    return Padding(
      padding: AppSpacing.page,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppToolbar(
            title: widget.title,
            subtitle: widget.subtitle,
            filters: [
              if (widget.showSearch)
                AppSearchField(
                  controller: _searchController,
                  hint: widget.searchHint,
                  onChanged: _onSearchChanged,
                ),
            ],
            actions: [
              if (widget.canWrite)
                FilledButton.icon(
                  onPressed: widget.onAdd,
                  icon: const Icon(Icons.add_rounded, size: 18),
                  label: Text(widget.addLabel),
                ),
            ],
          ),
          if (widget.stats.isNotEmpty) ...[_statsRow(widget.stats), const SizedBox(height: AppSpacing.md)],
          if (widget.expand) Expanded(child: table) else table,
        ],
      ),
    );
  }

  /// Equal-width KPI cards. Wraps rather than overflowing so a narrow window
  /// stacks them instead of clipping the last one.
  Widget _statsRow(List<Widget> stats) {
    return LayoutBuilder(
      builder: (context, constraints) {
        const minCardWidth = 220.0;
        final perRow = (constraints.maxWidth / minCardWidth).floor().clamp(1, stats.length);
        final width = (constraints.maxWidth - (perRow - 1) * AppSpacing.md) / perRow;

        return Wrap(
          spacing: AppSpacing.md,
          runSpacing: AppSpacing.md,
          children: [for (final stat in stats) SizedBox(width: width, child: stat)],
        );
      },
    );
  }
}
