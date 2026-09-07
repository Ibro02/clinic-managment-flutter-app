import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../core/reports_api.dart';
import '../../models/medical_service.dart';
import '../../models/report_data.dart';
import '../../providers/medical_service_provider.dart';
import '../../widgets/charts/app_bar_chart.dart';
import '../../widgets/charts/chart_card.dart';
import '../../widgets/charts/chart_series.dart';
import '../../widgets/ui/app_states.dart';

/// Revenue report: a filterable PDF plus a chart of the same figures
/// (review item C8 - the tab used to offer period only, and no graphical view).
///
/// The chart and the PDF come from one server-side aggregation over the same
/// filter, so what is on screen and what prints can never disagree.
class RevenueReportTab extends StatefulWidget {
  const RevenueReportTab({super.key});

  @override
  State<RevenueReportTab> createState() => _RevenueReportTabState();
}

class _RevenueReportTabState extends State<RevenueReportTab> {
  late final ReportsApi _api;
  late final MedicalServiceProvider _serviceProvider;

  List<MedicalService> _services = [];
  int? _selectedServiceId;
  DateTime? _startDate;
  DateTime? _endDate;

  Uint8List? _pdfBytes;
  RevenueReportData? _data;
  String? _error;
  bool _isGenerating = false;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _api = ReportsApi(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _loadServices();
  }

  Future<void> _loadServices() async {
    try {
      final result = await _serviceProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
      if (mounted) setState(() => _services = result.resultList);
    } catch (_) {
      // "Sve usluge" still works if the dropdown itself failed to load, so this
      // is not worth an error banner over a report the user can still run.
    }
  }

  Future<void> _pickDate({required bool isStart}) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: (isStart ? _startDate : _endDate) ?? now,
      firstDate: DateTime(now.year - 5),
      lastDate: DateTime(now.year + 1),
    );
    if (picked == null) return;
    setState(() {
      if (isStart) {
        _startDate = picked;
      } else {
        _endDate = picked;
      }
    });
  }

  Future<void> _generate() async {
    final start = _startDate;
    final end = _endDate;
    if (start == null || end == null) {
      setState(() => _error = 'Odaberite početni i krajnji datum perioda prije generisanja izvještaja.');
      return;
    }
    if (start.isAfter(end)) {
      setState(() => _error = 'Krajnji datum mora biti isti ili nakon početnog datuma.');
      return;
    }

    setState(() {
      _isGenerating = true;
      _error = null;
    });
    try {
      // Both in flight at once: the chart and the document are two views of one
      // report, so waiting for the PDF before asking for the numbers would just
      // make the tab feel slower for no reason.
      final pdfRequest = _api.getRevenueReportPdf(
        startDate: start,
        endDate: end,
        medicalServiceId: _selectedServiceId,
      );
      final dataRequest = _api.getRevenueReportData(
        startDate: start,
        endDate: end,
        medicalServiceId: _selectedServiceId,
      );

      final pdfBytes = await pdfRequest;
      final data = await dataRequest;
      if (!mounted) return;
      setState(() {
        _pdfBytes = Uint8List.fromList(pdfBytes);
        _data = data;
      });
    } catch (e) {
      if (mounted) setState(() => _error = 'Izvještaj nije generisan. ${failureCause(e)}');
    } finally {
      if (mounted) setState(() => _isGenerating = false);
    }
  }

  String _dateLabel(DateTime? d) =>
      d == null ? 'Odaberite datum' : '${d.day.toString().padLeft(2, '0')}.${d.month.toString().padLeft(2, '0')}.${d.year}';

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 16,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Od: ${_dateLabel(_startDate)}'),
                onPressed: () => _pickDate(isStart: true),
              ),
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Do: ${_dateLabel(_endDate)}'),
                onPressed: () => _pickDate(isStart: false),
              ),
              // Populated from the DB, never a hardcoded list (rulebook Part II
              // §K), and the "all services" option is a real entry rather than
              // an empty selection the user has to guess at.
              SizedBox(
                width: 260,
                child: DropdownButtonFormField<int?>(
                  initialValue: _selectedServiceId,
                  decoration: const InputDecoration(labelText: 'Usluga', isDense: true),
                  items: [
                    const DropdownMenuItem<int?>(value: null, child: Text('Sve usluge')),
                    for (final service in _services)
                      DropdownMenuItem<int?>(value: service.id, child: Text(service.name)),
                  ],
                  onChanged: (value) => setState(() => _selectedServiceId = value),
                ),
              ),
              FilledButton.icon(
                icon: _isGenerating
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.picture_as_pdf_outlined),
                label: const Text('Generiraj'),
                onPressed: _isGenerating ? null : _generate,
              ),
            ],
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ],
          const SizedBox(height: 12),
          if (_pdfBytes != null) ...[
            Row(
              children: [
                OutlinedButton.icon(
                  icon: const Icon(Icons.print_outlined),
                  label: const Text('Ispis'),
                  onPressed: () => Printing.layoutPdf(
                    onLayout: (format) async => _pdfBytes!,
                    name: 'izvjestaj-prihodi.pdf',
                  ),
                ),
                const SizedBox(width: 8),
                OutlinedButton.icon(
                  icon: const Icon(Icons.download_outlined),
                  label: const Text('Preuzmi'),
                  onPressed: () => Printing.sharePdf(
                    bytes: _pdfBytes!,
                    filename: 'izvjestaj-prihodi.pdf',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Expanded(
              child: ListView(
                children: [
                  if (_data != null) ...[_chart(_data!), const SizedBox(height: AppSpacing.lg)],
                  SizedBox(
                    height: 640,
                    child: PdfPreview(
                      build: (format) async => _pdfBytes!,
                      useActions: false,
                      pdfFileName: 'izvjestaj-prihodi.pdf',
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _chart(RevenueReportData data) {
    return ChartCard(
      title: 'Prihod po usluzi',
      value: '${data.grandTotalEur.toStringAsFixed(2)} EUR',
      subtitle: 'Naplaćeno umanjeno za povrate, u odabranom periodu.',
      child: data.rows.isEmpty
          ? const AppEmptyState(
              icon: Icons.bar_chart_rounded,
              title: 'Nema prihoda za ovaj filter',
              message: 'U odabranom periodu nema naplaćenih uplata za odabranu uslugu.',
              compact: true,
            )
          : AppBarChart(
              height: 260,
              labels: [for (final row in data.rows) row.serviceName],
              series: [
                AppChartSeries(
                  name: 'Prihod (EUR)',
                  values: [for (final row in data.rows) row.netTotalEur],
                ),
              ],
              valueFormatter: (value) => value.toStringAsFixed(0),
            ),
    );
  }
}
