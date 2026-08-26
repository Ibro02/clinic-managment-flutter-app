import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';

class RevenueReportTab extends StatefulWidget {
  const RevenueReportTab({super.key});

  @override
  State<RevenueReportTab> createState() => _RevenueReportTabState();
}

class _RevenueReportTabState extends State<RevenueReportTab> {
  late final ReportsApi _api;

  DateTime? _startDate;
  DateTime? _endDate;
  Uint8List? _pdfBytes;
  String? _error;
  bool _isGenerating = false;

  @override
  void initState() {
    super.initState();
    _api = ReportsApi(context.read<AuthSession>());
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
      final bytes = await _api.getRevenueReportPdf(startDate: start, endDate: end);
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
            const SizedBox(height: 8),
            Expanded(
              child: PdfPreview(
                build: (format) async => _pdfBytes!,
                useActions: false,
                pdfFileName: 'izvjestaj-prihodi.pdf',
              ),
            ),
          ],
        ],
      ),
    );
  }
}
