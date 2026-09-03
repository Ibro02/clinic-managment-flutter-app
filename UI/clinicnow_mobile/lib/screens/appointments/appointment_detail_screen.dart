import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import '../../core/design_tokens.dart';
import '../../providers/payment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';
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

  /// Status as a semantic tone - the same mapping the list screen and the
  /// desktop app use, so one appointment never reads as two different things.
  AppTone _statusTone(int status) => switch (status) {
    0 => AppTone.warning, // Na čekanju
    1 => AppTone.info, // Potvrđen
    2 => AppTone.success, // Završen
    3 => AppTone.danger, // Otkazan
    _ => AppTone.neutral,
  };

  Future<void> _cancel() async {
    final reasonController = TextEditingController();
    String? reasonError;

    final reason = await showAppDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Otkazivanje termina',
          subtitle: 'Jeste li sigurni da želite otkazati ovaj termin?',
          icon: Icons.cancel_outlined,
          tone: AppTone.danger,
          actions: [
            OutlinedButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              style: FilledButton.styleFrom(
                backgroundColor: dialogContext.colors.danger,
                foregroundColor: Colors.white,
              ),
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
          child: AppField(
            label: 'Razlog otkazivanja',
            required: true,
            child: TextField(
              controller: reasonController,
              decoration: InputDecoration(errorText: reasonError),
              maxLines: 3,
            ),
          ),
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
      body: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          AppCard(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                AppStatusBadge(
                  label: _appointment.statusName,
                  tone: _statusTone(_appointment.status),
                ),
                const SizedBox(height: AppSpacing.md),
                _DetailRow(label: 'Datum i vrijeme', value: _dateTimeFormat.format(_appointment.startUtc)),
                _DetailRow(label: 'Doktor', value: _appointment.doctorName),
                _DetailRow(label: 'Usluga', value: _appointment.medicalServiceName),
                _DetailRow(label: 'Lokacija', value: _appointment.locationName),
                if (_appointment.cancellationReason != null)
                  _DetailRow(label: 'Razlog otkazivanja', value: _appointment.cancellationReason!),
              ],
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          // Rulebook §J: once paid, the UI states it plainly and the pay button
          // is gone - never a second chance to pay the same thing twice.
          if (_appointment.isPaid)
            AppNotice(
              tone: AppTone.success,
              message: _appointment.paymentStatus ?? 'Plaćeno',
            )
          else if (_appointment.status != 3) // never offer to pay a Cancelled appointment
            FilledButton.icon(
              onPressed: _isPaying ? null : _pay,
              icon: _isPaying
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.payment),
              label: const Text('Plati'),
            ),
          const SizedBox(height: AppSpacing.lg),
          // Rulebook §K: when cancelling isn't allowed, say why rather than
          // hiding the control and leaving the patient guessing.
          if (_appointment.canCancel)
            FilledButton.icon(
              onPressed: _isCancelling ? null : _cancel,
              style: FilledButton.styleFrom(
                backgroundColor: context.colors.danger,
                foregroundColor: Colors.white,
              ),
              icon: _isCancelling
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.cancel_outlined),
              label: const Text('Otkaži termin'),
            )
          else
            AppNotice(
              tone: AppTone.neutral,
              message: _appointment.status == 3
                  ? 'Termin je već otkazan.'
                  : _appointment.status == 2
                  ? 'Završen termin se ne može otkazati.'
                  : 'Otkazivanje nije moguće manje od 48 sati prije termina.',
            ),
        ],
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
