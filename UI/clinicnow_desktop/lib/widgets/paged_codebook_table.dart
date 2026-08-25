import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_exception.dart';
import '../core/base_provider.dart';
import '../models/paged_result.dart';

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
class PagedCodebookTable<T> extends StatefulWidget {
  final String title;
  final String searchHint;
  final BaseProvider<T> provider;
  final List<DataColumn> Function() buildColumns;
  final List<DataCell> Function(T item) buildCells;
  final VoidCallback onAdd;
  final void Function(T item) onEdit;
  final Future<void> Function(T item) onDelete;
  final String Function(T item) itemLabel;

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

  const PagedCodebookTable({
    super.key,
    required this.title,
    required this.searchHint,
    required this.provider,
    required this.buildColumns,
    required this.buildCells,
    required this.onAdd,
    required this.onEdit,
    required this.onDelete,
    required this.itemLabel,
    this.orderBy = 'Name',
    this.extraSearchParams = const {},
    this.canWrite = true,
    this.extraRowActions,
    this.showSearch = true,
  });

  @override
  State<PagedCodebookTable<T>> createState() => PagedCodebookTableState<T>();
}

class PagedCodebookTableState<T> extends State<PagedCodebookTable<T>> {
  static const int _pageSize = 10;

  final _searchController = TextEditingController();
  Timer? _debounce;

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
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Potvrda brisanja'),
        content: Text('Da li ste sigurni da želite obrisati "${widget.itemLabel(item)}"?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Odustani'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
            child: const Text('Obriši'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    try {
      await widget.onDelete(item);
      await load();
    } on ApiException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  int get _totalPages {
    final count = _result?.count ?? 0;
    return count == 0 ? 1 : ((count - 1) ~/ _pageSize) + 1;
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
              ),
              if (widget.canWrite)
                FilledButton.icon(
                  onPressed: widget.onAdd,
                  icon: const Icon(Icons.add),
                  label: const Text('Dodaj'),
                ),
            ],
          ),
          if (widget.showSearch) ...[
            const SizedBox(height: 12),
            TextField(
              controller: _searchController,
              decoration: InputDecoration(
                labelText: widget.searchHint,
                prefixIcon: const Icon(Icons.search),
                isDense: true,
                border: const OutlineInputBorder(),
              ),
              onChanged: _onSearchChanged,
            ),
          ],
          const SizedBox(height: 12),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ),
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator())
                : (_result == null || _result!.resultList.isEmpty)
                    ? const Center(child: Text('Nema podataka za prikaz.'))
                    : SingleChildScrollView(
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: [
                              ...widget.buildColumns(),
                              const DataColumn(label: Text('Akcije')),
                            ],
                            rows: _result!.resultList
                                .map((item) => DataRow(cells: [
                                      ...widget.buildCells(item),
                                      DataCell(Row(
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          ...?widget.extraRowActions?.call(item),
                                          if (widget.canWrite) ...[
                                            IconButton(
                                              tooltip: 'Uredi',
                                              icon: const Icon(Icons.edit_outlined),
                                              onPressed: () => widget.onEdit(item),
                                            ),
                                            IconButton(
                                              tooltip: 'Obriši',
                                              icon: const Icon(Icons.delete_outline),
                                              onPressed: () => _confirmDelete(item),
                                            ),
                                          ],
                                        ],
                                      )),
                                    ]))
                                .toList(),
                          ),
                        ),
                      ),
          ),
          const SizedBox(height: 8),
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              IconButton(
                icon: const Icon(Icons.chevron_left),
                onPressed: _page > 1 ? () { setState(() => _page--); load(); } : null,
              ),
              Text('Strana $_page od $_totalPages'),
              IconButton(
                icon: const Icon(Icons.chevron_right),
                onPressed: _page < _totalPages ? () { setState(() => _page++); load(); } : null,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
