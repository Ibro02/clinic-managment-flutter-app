import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/payment_provider.dart';
import '../payments/payment_webview_screen.dart';

/// Detail view of a single appointment, with the Cancel action - only shown
/// (rulebook Part II §K: "disabled-with-reason for unavailable actions") when
/// the server's own `allowedActions` says it's legal (e.g. blocked once inside
/// the 48h cutoff, or once the appointment is already Completed/Cancelled).
class AppointmentDetailScreen extends StatefulWidget {
  final Appointment appointment;
  final AppointmentProvider provider;

  const AppointmentDetailScreen({super.key, required this.appointment, required this.provider});

  @override
  State<AppointmentDetailScreen> createState() => _AppointmentDetailScreenState();
}

class _AppointmentDetailScreenState extends State<AppointmentDetailScreen> {
  static final _dateTimeFormat = DateFormat('EEEE, dd.MM.yyyy HH:mm');

  late Appointment _appointment;
  bool _isCancelling = false;

  late final PaymentProvider _paymentProvider;
  bool _isPaying = false;

  @override
  void initState() {
    super.initState();
    _appointment = widget.appointment;
    _paymentProvider = PaymentProvider(context.read<AuthSession>());
  }

  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };

  Future<void> _cancel() async {
    final reasonController = TextEditingController();
    String? reasonError;

    final reason = await showDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Otkazivanje termina'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Jeste li sigurni da želite otkazati ovaj termin?'),
              const SizedBox(height: 16),
              TextField(
                controller: reasonController,
                decoration: InputDecoration(labelText: 'Razlog otkazivanja', errorText: reasonError),
                maxLines: 2,
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
              onPressed: () {
                if (reasonController.text.trim().isEmpty) {
                  setDialogState(() => reasonError = 'Razlog otkazivanja je obavezan.');
                  return;
                }
                Navigator.of(dialogContext).pop(reasonController.text.trim());
              },
              child: const Text('Otkaži termin'),
            ),
          ],
        ),
      ),
    );

    if (reason == null) return;

    setState(() => _isCancelling = true);
    try {
      final updated = await widget.provider.cancel(_appointment.id, reason);
      if (!mounted) return;
      setState(() => _appointment = updated);
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Termin je otkazan.')));
    } on ApiException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _isCancelling = false);
    }
  }

  Future<void> _pay() async {
    setState(() => _isPaying = true);
    try {
      final payment = await _paymentProvider.create(_appointment.id);
      if (!mounted || payment.approveUrl == null) return;

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      if (approved == true) {
        await _paymentProvider.capture(payment.id);
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Plaćanje uspješno.')));
        // Re-fetch to pick up the server's fresh isPaid/paymentStatus.
        final refreshed = await widget.provider.getById(_appointment.id);
        if (mounted) setState(() => _appointment = refreshed);
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _isPaying = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Detalji termina')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Chip(
              label: Text(_appointment.statusName, style: const TextStyle(color: Colors.white)),
              backgroundColor: _statusColor(_appointment.status),
            ),
            const SizedBox(height: 16),
            _DetailRow(label: 'Datum i vrijeme', value: _dateTimeFormat.format(_appointment.startUtc)),
            _DetailRow(label: 'Doktor', value: _appointment.doctorName),
            _DetailRow(label: 'Usluga', value: _appointment.medicalServiceName),
            _DetailRow(label: 'Lokacija', value: _appointment.locationName),
            if (_appointment.cancellationReason != null)
              _DetailRow(label: 'Razlog otkazivanja', value: _appointment.cancellationReason!),
            const SizedBox(height: 8),
            if (_appointment.isPaid)
              Chip(
                avatar: const Icon(Icons.check_circle, color: Colors.white, size: 18),
                label: Text(_appointment.paymentStatus ?? 'Plaćeno', style: const TextStyle(color: Colors.white)),
                backgroundColor: Colors.green,
              )
            else if (_appointment.status != 3) // never offer to pay a Cancelled appointment
              FilledButton.icon(
                onPressed: _isPaying ? null : _pay,
                icon: _isPaying
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.payment),
                label: const Text('Plati'),
              ),
            const SizedBox(height: 24),
            if (_appointment.canCancel)
              FilledButton.icon(
                onPressed: _isCancelling ? null : _cancel,
                style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
                icon: _isCancelling
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.cancel_outlined),
                label: const Text('Otkaži termin'),
              )
            else
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(
                  _appointment.status == 3
                      ? 'Termin je već otkazan.'
                      : _appointment.status == 2
                          ? 'Završen termin se ne može otkazati.'
                          : 'Otkazivanje nije moguće manje od 48 sati prije termina.',
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _DetailRow extends StatelessWidget {
  final String label;
  final String value;

  const _DetailRow({required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 140, child: Text(label, style: Theme.of(context).textTheme.labelLarge)),
          Expanded(child: Text(value)),
        ],
      ),
    );
  }
}
