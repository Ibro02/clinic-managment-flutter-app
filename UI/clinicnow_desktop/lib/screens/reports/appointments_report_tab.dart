import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../core/reports_api.dart';
import '../../models/doctor.dart';
import '../../models/report_data.dart';
import '../../providers/doctor_provider.dart';
import '../../widgets/charts/app_bar_chart.dart';
import '../../widgets/charts/chart_card.dart';
import '../../widgets/charts/chart_series.dart';
import '../../widgets/ui/app_states.dart';

class AppointmentsReportTab extends StatefulWidget {
  const AppointmentsReportTab({super.key});

  @override
  State<AppointmentsReportTab> createState() => _AppointmentsReportTabState();
}

class _AppointmentsReportTabState extends State<AppointmentsReportTab> {
  static const _statusOptions = <int, String>{0: 'Na čekanju', 1: 'Potvrđen', 2: 'Završen', 3: 'Otkazan'};

  late final ReportsApi _api;
  late final DoctorProvider _doctorProvider;

  List<Doctor> _doctors = [];
  int? _selectedDoctorId;
  final Set<int> _selectedStatuses = {};
  DateTime? _startDate;
  DateTime? _endDate;

  Uint8List? _pdfBytes;
  AppointmentsReportData? _data;
  String? _error;
  bool _isGenerating = false;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _api = ReportsApi(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _loadDoctors();
  }

  Future<void> _loadDoctors() async {
    try {
      final result = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'User.LastName'});
      if (mounted) setState(() => _doctors = result.resultList);
    } on ApiException {
      // "Svi doktori" still works even if the dropdown list itself failed to load.
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
      setState(() => _error = 'Odaberite period izvještaja.');
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
    final statuses = _selectedStatuses.isEmpty ? null : _selectedStatuses.toList();
    try {
      // Both in flight at once: the chart and the document are two views of one
      // report, so waiting for the PDF before asking for the numbers would just
      // make the tab feel slower for no reason.
      final pdfRequest = _api.getAppointmentsReportPdf(
        startDate: start,
        endDate: end,
        doctorId: _selectedDoctorId,
        statuses: statuses,
      );
      final dataRequest = _api.getAppointmentsReportData(
        startDate: start,
        endDate: end,
        doctorId: _selectedDoctorId,
        statuses: statuses,
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
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int?>(
                  initialValue: _selectedDoctorId,
                  decoration: const InputDecoration(labelText: 'Doktor'),
                  items: [
                    const DropdownMenuItem<int?>(value: null, child: Text('Svi doktori')),
                    for (final doctor in _doctors) DropdownMenuItem<int?>(value: doctor.id, child: Text(doctor.fullName)),
                  ],
                  onChanged: (value) => setState(() => _selectedDoctorId = value),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            children: [
              for (final entry in _statusOptions.entries)
                FilterChip(
                  label: Text(entry.value),
                  selected: _selectedStatuses.contains(entry.key),
                  onSelected: (selected) => setState(() {
                    if (selected) {
                      _selectedStatuses.add(entry.key);
                    } else {
                      _selectedStatuses.remove(entry.key);
                    }
                  }),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text('Bez odabranog statusa = svi statusi.', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton.icon(
                icon: _isGenerating
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.picture_as_pdf_outlined),
                label: const Text('Generiraj'),
                onPressed: _isGenerating ? null : _generate,
              ),
              if (_error != null) ...[
                const SizedBox(width: 12),
                Expanded(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
              ],
            ],
          ),
          const SizedBox(height: 12),
          if (_pdfBytes != null) ...[
            Row(
              children: [
                OutlinedButton.icon(
                  icon: const Icon(Icons.print_outlined),
                  label: const Text('Ispis'),
                  onPressed: () => Printing.layoutPdf(
                    onLayout: (format) async => _pdfBytes!,
                    name: 'izvjestaj-termini.pdf',
                  ),
                ),
                const SizedBox(width: 8),
                OutlinedButton.icon(
                  icon: const Icon(Icons.download_outlined),
                  label: const Text('Preuzmi'),
                  onPressed: () => Printing.sharePdf(
                    bytes: _pdfBytes!,
                    filename: 'izvjestaj-termini.pdf',
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
                      pdfFileName: 'izvjestaj-termini.pdf',
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

  /// Grouped bars per doctor, one series per status - the same split the PDF
  /// prints, so "who is busy, and with what" is readable at a glance instead of
  /// only by counting rows in the document.
  Widget _chart(AppointmentsReportData data) {
    return ChartCard(
      title: 'Termini po doktoru',
      value: '${data.totalCount}',
      subtitle: 'Ukupan broj termina u odabranom periodu i filterima.',
      child: data.rows.isEmpty
          ? const AppEmptyState(
              icon: Icons.bar_chart_rounded,
              title: 'Nema termina za ove filtere',
              message: 'U odabranom periodu nema termina koji odgovaraju odabranom doktoru i statusima.',
              compact: true,
            )
          : AppBarChart(
              height: 280,
              labels: [for (final row in data.rows) row.doctorName],
              series: [
                AppChartSeries(name: 'Na čekanju', values: [for (final r in data.rows) r.pendingCount.toDouble()]),
                AppChartSeries(name: 'Potvrđeni', values: [for (final r in data.rows) r.confirmedCount.toDouble()]),
                AppChartSeries(name: 'Završeni', values: [for (final r in data.rows) r.completedCount.toDouble()]),
                AppChartSeries(name: 'Otkazani', values: [for (final r in data.rows) r.cancelledCount.toDouble()]),
              ],
              valueFormatter: (value) => value.toStringAsFixed(0),
            ),
    );
  }
}
