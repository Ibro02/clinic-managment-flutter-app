import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';
import '../../models/doctor.dart';
import '../../providers/doctor_provider.dart';

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
      final result = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
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

    setState(() {
      _isGenerating = true;
      _error = null;
    });
    try {
      final bytes = await _api.getAppointmentsReportPdf(
        startDate: start,
        endDate: end,
        doctorId: _selectedDoctorId,
        statuses: _selectedStatuses.isEmpty ? null : _selectedStatuses.toList(),
      );
      if (mounted) setState(() => _pdfBytes = Uint8List.fromList(bytes));
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
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
          if (_pdfBytes != null)
            Expanded(
              child: PdfPreview(
                build: (format) async => _pdfBytes!,
                canChangeOrientation: false,
                canChangePageFormat: false,
                canDebug: false,
                pdfFileName: 'izvjestaj-termini.pdf',
              ),
            ),
        ],
      ),
    );
  }
}
