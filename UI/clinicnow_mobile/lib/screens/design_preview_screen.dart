import 'package:flutter/material.dart';

import '../core/design_tokens.dart';
import '../widgets/charts/app_area_chart.dart';
import '../widgets/charts/app_bar_chart.dart';
import '../widgets/charts/app_donut_chart.dart';
import '../widgets/charts/chart_card.dart';
import '../widgets/charts/chart_series.dart';
import '../widgets/ui/app_badge.dart';
import '../widgets/ui/app_card.dart';
import '../widgets/ui/app_data_table.dart';
import '../widgets/ui/app_dialog.dart';
import '../widgets/ui/app_fields.dart';
import '../widgets/ui/app_states.dart';
import '../widgets/ui/stat_card.dart';

/// A throwaway screen that renders every component in the design system with
/// sample data, so the visual direction can be reviewed before it is wired into
/// real screens.
///
/// To view it, temporarily point `main.dart` at it:
///   home: const DesignPreviewScreen(),
///
/// Delete this file once the real screens are migrated. Nothing else imports it.
class DesignPreviewScreen extends StatefulWidget {
  const DesignPreviewScreen({super.key});

  @override
  State<DesignPreviewScreen> createState() => _DesignPreviewScreenState();
}

// --- sample data -------------------------------------------------------------

enum _Status { confirmed, pending, cancelled, done }

class _Row {
  final String patient;
  final String doctor;
  final String service;
  final DateTime when;
  final double price;
  final _Status status;

  const _Row(this.patient, this.doctor, this.service, this.when, this.price, this.status);
}

class _DesignPreviewScreenState extends State<DesignPreviewScreen> {
  String _range = '30d';
  String _sortKey = 'when';
  bool _sortAscending = true;
  int _page = 0;
  bool _loading = false;

  static const _pageSize = 6;

  final _rows = <_Row>[
    _Row('Amina Hodžić', 'dr. Selma Karić', 'Kardiološki pregled', DateTime(2026, 8, 27, 9, 0), 120, _Status.confirmed),
    _Row('Emir Begić', 'dr. Tarik Zolj', 'Ultrazvuk abdomena', DateTime(2026, 8, 27, 9, 30), 90, _Status.confirmed),
    _Row('Lejla Softić', 'dr. Selma Karić', 'Kontrolni pregled', DateTime(2026, 8, 27, 10, 15), 60, _Status.pending),
    _Row('Vedad Muminović', 'dr. Ines Delić', 'Laboratorija', DateTime(2026, 8, 27, 11, 0), 45, _Status.done),
    _Row('Nedim Alispahić', 'dr. Tarik Zolj', 'EKG', DateTime(2026, 8, 27, 11, 45), 70, _Status.cancelled),
    _Row('Ajla Redžić', 'dr. Ines Delić', 'Dermatološki pregled', DateTime(2026, 8, 27, 12, 30), 110, _Status.confirmed),
    _Row('Haris Čolaković', 'dr. Selma Karić', 'Sistematski pregled', DateTime(2026, 8, 28, 8, 30), 210, _Status.pending),
    _Row('Dženana Kurtović', 'dr. Ines Delić', 'Kontrolni pregled', DateTime(2026, 8, 28, 9, 15), 60, _Status.confirmed),
    _Row('Faruk Imamović', 'dr. Tarik Zolj', 'Spirometrija', DateTime(2026, 8, 28, 10, 0), 85, _Status.done),
    _Row('Merima Bajrić', 'dr. Selma Karić', 'Holter', DateTime(2026, 8, 28, 10, 45), 150, _Status.confirmed),
  ];

  List<_Row> get _sorted {
    final list = [..._rows];
    list.sort((a, b) {
      final result = switch (_sortKey) {
        'patient' => a.patient.compareTo(b.patient),
        'doctor' => a.doctor.compareTo(b.doctor),
        'price' => a.price.compareTo(b.price),
        _ => a.when.compareTo(b.when),
      };
      return _sortAscending ? result : -result;
    });
    return list;
  }

  List<_Row> get _pageRows {
    final list = _sorted;
    final start = _page * _pageSize;
    if (start >= list.length) return const [];
    return list.sublist(start, (start + _pageSize).clamp(0, list.length));
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Scaffold(
      backgroundColor: c.canvas,
      body: SingleChildScrollView(
        padding: AppSpacing.page,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const AppSectionHeader(label: 'Ključne brojke'),
            _kpis(context),
            const SizedBox(height: AppSpacing.lg),
            const AppSectionHeader(label: 'Analitika'),
            _charts(context),
            const SizedBox(height: AppSpacing.lg),
            const AppSectionHeader(label: 'Termini'),
            _tableToolbar(context),
            SizedBox(height: 460, child: _table(context)),
            const SizedBox(height: AppSpacing.lg),
            const AppSectionHeader(label: 'Dijalozi i stanja'),
            _dialogsAndStates(context),
            const SizedBox(height: AppSpacing.xxl),
          ],
        ),
      ),
    );
  }

  // --- KPI row ---------------------------------------------------------------

  Widget _kpis(BuildContext context) {
    return _responsiveRow(
      context,
      minTileWidth: 260,
      children: [
        const StatCard(
          label: 'Termini danas',
          value: '42',
          icon: Icons.event_available_rounded,
          tone: AppTone.primary,
          deltaPercent: 12.4,
          caption: '6 još nije potvrđeno',
          trend: [22, 26, 24, 31, 29, 35, 33, 38, 42],
        ),
        const StatCard(
          label: 'Novi pacijenti',
          value: '18',
          icon: Icons.person_add_alt_1_rounded,
          tone: AppTone.success,
          deltaPercent: 8.1,
          caption: 'ove sedmice',
          trend: [8, 11, 9, 14, 12, 15, 16, 18],
        ),
        const StatCard(
          label: 'Otkazani termini',
          value: '5',
          icon: Icons.event_busy_rounded,
          tone: AppTone.danger,
          deltaPercent: 3.2,
          higherIsBetter: false,
          caption: '11.9% od ukupnog broja',
          trend: [2, 3, 2, 4, 3, 5, 4, 5],
        ),
        const StatCard(
          label: 'Prihod (30 dana)',
          value: '24.860 KM',
          icon: Icons.payments_rounded,
          tone: AppTone.info,
          deltaPercent: 15.7,
          caption: 'naplaćeno 21.340 KM',
          trend: [620, 740, 690, 880, 820, 960, 1040, 1120],
        ),
      ],
    );
  }

  // --- charts ----------------------------------------------------------------

  Widget _charts(BuildContext context) {
    final labels = List.generate(14, (i) => '${i + 14}.8');

    final scheduled = <double>[28, 31, 26, 34, 38, 22, 12, 36, 41, 39, 44, 47, 25, 14];
    final completed = <double>[24, 28, 22, 30, 33, 19, 10, 31, 36, 34, 39, 41, 22, 12];

    final areaCard = ChartCard(
      title: 'Termini kroz vrijeme',
      subtitle: 'Zakazani naspram realizovanih',
      value: '437',
      deltaPercent: 12.4,
      legend: [
        AppChartSeries(name: 'Zakazani', values: scheduled, color: context.colors.series(0)),
        AppChartSeries(name: 'Realizovani', values: completed, color: context.colors.series(1)),
      ],
      trailing: AppSegmented<String>(
        options: const [('7d', '7 dana'), ('30d', '30 dana'), ('90d', '90 dana')],
        selected: _range,
        onChanged: (value) => setState(() => _range = value),
      ),
      child: AppAreaChart(
        labels: labels,
        series: [
          AppChartSeries(name: 'Zakazani', values: scheduled),
          AppChartSeries(name: 'Realizovani', values: completed, color: context.colors.series(1)),
        ],
      ),
    );

    final donutCard = ChartCard(
      title: 'Struktura po uslugama',
      subtitle: 'Udio u ukupnom prihodu',
      child: AppDonutChart(
        centerLabel: 'Ukupno',
        valueFormatter: ChartFormat.money,
        slices: const [
          AppChartSlice(name: 'Kardiologija', value: 8420),
          AppChartSlice(name: 'Dermatologija', value: 5310),
          AppChartSlice(name: 'Laboratorija', value: 4180),
          AppChartSlice(name: 'Ultrazvuk', value: 3960),
          AppChartSlice(name: 'Ostalo', value: 2990),
        ],
      ),
    );

    final barCard = ChartCard(
      title: 'Prihod po lokaciji',
      subtitle: 'Posljednjih 6 mjeseci',
      value: '24.860 KM',
      deltaPercent: 15.7,
      legend: [
        AppChartSeries(name: 'Mostar', values: const [], color: context.colors.series(0)),
        AppChartSeries(name: 'Sarajevo', values: const [], color: context.colors.series(2)),
      ],
      child: AppBarChart(
        labels: const ['Mar', 'Apr', 'Maj', 'Jun', 'Jul', 'Aug'],
        valueFormatter: ChartFormat.money,
        series: [
          const AppChartSeries(name: 'Mostar', values: [3200, 3900, 3600, 4400, 4100, 4800]),
          AppChartSeries(
            name: 'Sarajevo',
            values: const [2100, 2600, 2900, 2700, 3300, 3600],
            color: context.colors.series(2),
          ),
        ],
      ),
    );

    return Column(
      children: [
        LayoutBuilder(
          builder: (context, constraints) {
            if (constraints.maxWidth < 1080) {
              return Column(
                children: [areaCard, const SizedBox(height: AppSpacing.md), donutCard],
              );
            }
            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(flex: 3, child: areaCard),
                const SizedBox(width: AppSpacing.md),
                Expanded(flex: 2, child: donutCard),
              ],
            );
          },
        ),
        const SizedBox(height: AppSpacing.md),
        barCard,
      ],
    );
  }

  // --- table -----------------------------------------------------------------

  Widget _tableToolbar(BuildContext context) {
    return AppToolbar(
      filters: [
        AppSearchField(onChanged: (_) {}),
        AppSegmented<String>(
          options: const [('all', 'Svi'), ('today', 'Danas'), ('week', 'Sedmica')],
          selected: 'today',
          onChanged: (_) {},
        ),
      ],
      actions: [
        OutlinedButton.icon(
          onPressed: () => setState(() => _loading = !_loading),
          icon: const Icon(Icons.hourglass_empty_rounded, size: 17),
          label: Text(_loading ? 'Prikaži podatke' : 'Prikaži učitavanje'),
        ),
        FilledButton.icon(
          onPressed: () => _openFormDialog(context),
          icon: const Icon(Icons.add_rounded, size: 18),
          label: const Text('Novi termin'),
        ),
      ],
    );
  }

  Widget _table(BuildContext context) {
    final c = context.colors;

    return AppDataTable<_Row>(
      rows: _pageRows,
      isLoading: _loading,
      sortKey: _sortKey,
      sortAscending: _sortAscending,
      onSort: (key) => setState(() {
        if (_sortKey == key) {
          _sortAscending = !_sortAscending;
        } else {
          _sortKey = key;
          _sortAscending = true;
        }
      }),
      paging: AppTablePaging(page: _page, pageSize: _pageSize, totalCount: _rows.length),
      onPageChanged: (page) => setState(() => _page = page),
      onRowTap: (row) => _openDetailDialog(context, row),
      emptyTitle: 'Nema termina',
      emptyMessage: 'Za odabrani period nema zakazanih termina. Promijenite filter ili zakažite novi.',
      rowActions: (context, row) => [
        AppRowAction(icon: Icons.edit_outlined, tooltip: 'Uredi', onPressed: () => _openFormDialog(context)),
        AppRowAction(
          icon: Icons.delete_outline_rounded,
          tooltip: 'Otkaži termin',
          destructive: true,
          onPressed: () => _confirmCancel(context, row),
        ),
      ],
      columns: [
        AppColumn<_Row>(
          label: 'Pacijent',
          flex: 3,
          sortKey: 'patient',
          cell: (context, row) => Row(
            children: [
              AppAvatar(name: row.patient, size: 32),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(row.patient, style: context.text.titleSmall, overflow: TextOverflow.ellipsis),
                    Text(
                      row.service,
                      overflow: TextOverflow.ellipsis,
                      style: context.text.bodySmall?.copyWith(color: c.textMuted, fontSize: 12),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        AppColumn<_Row>(
          label: 'Doktor',
          flex: 2,
          sortKey: 'doctor',
          cell: (context, row) => Text(row.doctor),
        ),
        AppColumn<_Row>(
          label: 'Termin',
          width: 150,
          numeric: true,
          sortKey: 'when',
          cell: (context, row) => Text(
            '${row.when.day}.${row.when.month}. '
            '${row.when.hour.toString().padLeft(2, '0')}:${row.when.minute.toString().padLeft(2, '0')}',
          ),
        ),
        AppColumn<_Row>(
          label: 'Cijena',
          width: 110,
          numeric: true,
          sortKey: 'price',
          cell: (context, row) => Text(ChartFormat.money(row.price)),
        ),
        AppColumn<_Row>(
          label: 'Status',
          width: 140,
          cell: (context, row) => _statusBadge(row.status),
        ),
      ],
    );
  }

  Widget _statusBadge(_Status status) => switch (status) {
        _Status.confirmed => const AppStatusBadge(label: 'Potvrđen', tone: AppTone.success),
        _Status.pending => const AppStatusBadge(label: 'Na čekanju', tone: AppTone.warning),
        _Status.cancelled => const AppStatusBadge(label: 'Otkazan', tone: AppTone.danger),
        _Status.done => const AppStatusBadge(label: 'Realizovan', tone: AppTone.neutral),
      };

  // --- dialogs and states ----------------------------------------------------

  Widget _dialogsAndStates(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final cards = [
          AppCard(
            title: 'Dijalozi',
            subtitle: 'Sve iz istog okvira',
            icon: Icons.web_asset_rounded,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FilledButton.icon(
                  onPressed: () => _openFormDialog(context),
                  icon: const Icon(Icons.add_rounded, size: 18),
                  label: const Text('Forma za unos'),
                ),
                const SizedBox(height: AppSpacing.xs),
                OutlinedButton.icon(
                  onPressed: () => showConfirmDialog(
                    context: context,
                    title: 'Poslati podsjetnik?',
                    message: 'Pacijent će dobiti e-mail sa detaljima termina i uputama za dolazak.',
                    confirmLabel: 'Pošalji',
                  ),
                  icon: const Icon(Icons.mark_email_read_outlined, size: 17),
                  label: const Text('Potvrda'),
                ),
                const SizedBox(height: AppSpacing.xs),
                OutlinedButton.icon(
                  onPressed: () => showConfirmDialog(
                    context: context,
                    title: 'Obrisati pacijenta?',
                    message: 'Karton i sva pripadajuća dokumentacija bit će trajno uklonjeni. Ovo se ne može poništiti.',
                    confirmLabel: 'Obriši',
                    destructive: true,
                  ),
                  icon: const Icon(Icons.delete_outline_rounded, size: 17),
                  label: const Text('Destruktivna potvrda'),
                ),
              ],
            ),
          ),
          const AppCard(
            title: 'Prazna stanja',
            subtitle: 'Poziv na akciju, ne izvinjenje',
            icon: Icons.inbox_outlined,
            child: AppEmptyState(
              icon: Icons.folder_open_outlined,
              title: 'Nema dokumenata',
              message: 'Dodajte nalaz ili otpusno pismo da bi se pojavilo u kartonu pacijenta.',
              compact: true,
            ),
          ),
          const AppCard(
            title: 'Poruke',
            subtitle: 'Statusi i upozorenja',
            icon: Icons.info_outline_rounded,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                AppNotice(message: 'Radno vrijeme za subotu nije definisano za ovu lokaciju.', tone: AppTone.warning),
                SizedBox(height: AppSpacing.xs),
                AppNotice(message: 'Termin je uspješno zakazan i pacijent je obaviješten.', tone: AppTone.success),
                SizedBox(height: AppSpacing.sm),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  children: [
                    AppStatusBadge(label: 'Potvrđen', tone: AppTone.success),
                    AppStatusBadge(label: 'Na čekanju', tone: AppTone.warning),
                    AppStatusBadge(label: 'Otkazan', tone: AppTone.danger),
                    AppStatusBadge(label: 'Realizovan', tone: AppTone.neutral),
                  ],
                ),
              ],
            ),
          ),
        ];

        if (constraints.maxWidth < 900) {
          return Column(
            children: [
              for (var i = 0; i < cards.length; i++) ...[
                if (i > 0) const SizedBox(height: AppSpacing.md),
                cards[i],
              ],
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var i = 0; i < cards.length; i++) ...[
              if (i > 0) const SizedBox(width: AppSpacing.md),
              Expanded(child: cards[i]),
            ],
          ],
        );
      },
    );
  }

  void _openFormDialog(BuildContext context) {
    showAppDialog<void>(
      context: context,
      builder: (context) => AppDialog(
        title: 'Novi termin',
        subtitle: 'Popunite podatke i potvrdite zakazivanje',
        icon: Icons.event_available_rounded,
        actions: [
          OutlinedButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Odustani'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Zakaži termin'),
          ),
        ],
        child: AppFormSection(
          label: 'Osnovni podaci',
          children: [
            AppFieldRow(
              children: [
                AppField(
                  label: 'Pacijent',
                  required: true,
                  child: DropdownButtonFormField<String>(
                    initialValue: 'Amina Hodžić',
                    items: const [
                      DropdownMenuItem(value: 'Amina Hodžić', child: Text('Amina Hodžić')),
                      DropdownMenuItem(value: 'Emir Begić', child: Text('Emir Begić')),
                    ],
                    onChanged: (_) {},
                  ),
                ),
                AppField(
                  label: 'Doktor',
                  required: true,
                  child: DropdownButtonFormField<String>(
                    initialValue: 'dr. Selma Karić',
                    items: const [
                      DropdownMenuItem(value: 'dr. Selma Karić', child: Text('dr. Selma Karić')),
                      DropdownMenuItem(value: 'dr. Tarik Zolj', child: Text('dr. Tarik Zolj')),
                    ],
                    onChanged: (_) {},
                  ),
                ),
              ],
            ),
            AppFieldRow(
              children: [
                AppField(
                  label: 'Datum',
                  required: true,
                  child: TextFormField(
                    initialValue: '27.08.2026.',
                    decoration: const InputDecoration(suffixIcon: Icon(Icons.calendar_today_rounded, size: 17)),
                  ),
                ),
                AppField(
                  label: 'Vrijeme',
                  required: true,
                  help: 'Slobodni termini se provjeravaju automatski.',
                  child: TextFormField(
                    initialValue: '09:30',
                    decoration: const InputDecoration(suffixIcon: Icon(Icons.schedule_rounded, size: 17)),
                  ),
                ),
              ],
            ),
            AppField(
              label: 'Napomena',
              help: 'Vidljiva samo osoblju klinike.',
              child: TextFormField(
                maxLines: 3,
                decoration: const InputDecoration(hintText: 'Razlog dolaska, prethodna terapija…'),
              ),
            ),
            const AppNotice(
              message: 'Pacijent će primiti potvrdu e-mailom odmah nakon zakazivanja.',
              tone: AppTone.info,
            ),
          ],
        ),
      ),
    );
  }

  void _openDetailDialog(BuildContext context, _Row row) {
    showAppDialog<void>(
      context: context,
      builder: (context) {
        final c = context.colors;

        Widget line(String label, String value) => Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.sm),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SizedBox(
                    width: 130,
                    child: Text(label, style: context.text.bodySmall?.copyWith(color: c.textMuted)),
                  ),
                  Expanded(child: Text(value, style: context.text.titleSmall)),
                ],
              ),
            );

        return AppDialog(
          title: row.patient,
          subtitle: row.service,
          icon: Icons.person_outline_rounded,
          width: 520,
          actions: [
            OutlinedButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('Zatvori'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('Otvori karton'),
            ),
          ],
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              line('Doktor', row.doctor),
              line('Termin', '${row.when.day}.${row.when.month}.${row.when.year}. u '
                  '${row.when.hour.toString().padLeft(2, '0')}:${row.when.minute.toString().padLeft(2, '0')}'),
              line('Cijena', ChartFormat.money(row.price)),
              Row(
                children: [
                  SizedBox(
                    width: 130,
                    child: Text('Status', style: context.text.bodySmall?.copyWith(color: c.textMuted)),
                  ),
                  _statusBadge(row.status),
                ],
              ),
            ],
          ),
        );
      },
    );
  }

  Future<void> _confirmCancel(BuildContext context, _Row row) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Otkazati termin?',
      message: '${row.patient} će biti obaviješten/a da je termin '
          '${row.when.day}.${row.when.month}. otkazan.',
      confirmLabel: 'Otkaži termin',
      destructive: true,
    );

    if (confirmed && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Termin je otkazan.')),
      );
    }
  }

  // --- layout helper ---------------------------------------------------------

  /// Lays children into as many equal columns as fit at [minTileWidth],
  /// wrapping into further rows. Keeps the KPI strip from squashing four tiles
  /// into a narrow window.
  Widget _responsiveRow(
    BuildContext context, {
    required List<Widget> children,
    required double minTileWidth,
  }) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = (constraints.maxWidth / minTileWidth).floor().clamp(1, children.length);
        final rows = <Widget>[];

        for (var start = 0; start < children.length; start += columns) {
          final slice = children.sublist(start, (start + columns).clamp(0, children.length));

          rows.add(
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (var i = 0; i < columns; i++) ...[
                  if (i > 0) const SizedBox(width: AppSpacing.md),
                  Expanded(child: i < slice.length ? slice[i] : const SizedBox.shrink()),
                ],
              ],
            ),
          );

          if (start + columns < children.length) {
            rows.add(const SizedBox(height: AppSpacing.md));
          }
        }

        return Column(children: rows);
      },
    );
  }
}
