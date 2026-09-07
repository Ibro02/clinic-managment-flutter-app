import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/auth_session.dart';
import '../../core/error_text.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import '../../core/design_tokens.dart';
import '../../providers/payment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';
import '../payments/payment_webview_screen.dart';
import 'reschedule_appointment_screen.dart';

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
    } catch (e) {
      // Catches the transport failure too, not just ApiException: losing the
      // network mid-cancel used to leave the button spinning down with nothing
      // said, and the patient with no idea whether the termin was cancelled.
      if (!mounted) return;
      _showFailure('Termin nije otkazan.', e, stillTrue: 'Termin je i dalje zakazan.');
    } finally {
      if (mounted) setState(() => _isCancelling = false);
    }
  }

  Future<void> _reschedule() async {
    final moved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => RescheduleAppointmentScreen(appointment: _appointment, provider: widget.provider),
      ),
    );
    if (moved != true || !mounted) return;

    // The move already succeeded server-side; this is only the re-read. Failing
    // it must not read as "premještanje nije uspjelo" - it means the screen is
    // showing stale times, which is a different problem with a different fix.
    try {
      final refreshed = await widget.provider.getById(_appointment.id);
      if (mounted) setState(() => _appointment = refreshed);
    } catch (e) {
      if (mounted) {
        _showFailure(
          'Termin je premješten, ali prikaz nije osvježen.',
          e,
          stillTrue: 'Vratite se na listu termina da vidite novo vrijeme.',
        );
      }
    }
  }

  /// Bosnian decimal comma, without pulling in locale data the app doesn't
  /// otherwise initialise.
  static String _money(double amount) => amount.toStringAsFixed(2).replaceAll('.', ',');

  /// Rulebook Part II §K: an irreversible action asks first (review item C18).
  /// Paying is the most irreversible thing a patient can do in this app, so the
  /// dialog states the exact sum, the currency PayPal will charge in, and that
  /// undoing it is not something they can do themselves - the three facts that
  /// decide whether "Plati" was a mistake.
  Future<bool> _confirmPayment() async {
    final amountLine = _appointment.priceKm > 0
        ? 'Iznos: ${_money(_appointment.priceKm)} KM '
              '(naplaćuje se ${_money(_appointment.payableAmountEur)} EUR preko PayPala).\n\n'
        // Zero means the API didn't send a price (an older build). Better to
        // say the amount will be shown on the next screen than to invent one.
        : 'Tačan iznos će vam PayPal prikazati prije potvrde plaćanja.\n\n';

    return showConfirmDialog(
      context: context,
      title: 'Potvrda plaćanja',
      icon: Icons.payment_rounded,
      confirmLabel: 'Nastavi na PayPal',
      message:
          'Plaćate uslugu "${_appointment.medicalServiceName}" '
          'kod ${_appointment.doctorName}.\n\n'
          '$amountLine'
          'Sredstva se naplaćuju odmah nakon što odobrite plaćanje na PayPalu. '
          'Povrat nakon toga možete zatražiti samo od klinike.',
    );
  }

  Future<void> _pay() async {
    if (!await _confirmPayment()) return;
    if (!mounted) return;

    setState(() => _isPaying = true);
    // Tracked outside the try so *every* exit that isn't a completed payment
    // retires the attempt (review item C12) - including a capture PayPal
    // refuses. An attempt left open blocks the next "Plati" until the server's
    // staleness window expires, and by then the PayPal screen is gone, so the
    // patient has no way to clear it themselves.
    int? attemptId;
    // The one fact that decides which story the rest of this method may tell.
    // Everything before the capture can honestly promise "ništa vam nije
    // naplaćeno"; nothing after it may, so the post-capture re-read is kept
    // outside the try that owns that promise.
    var captured = false;

    try {
      final payment = await _paymentProvider.create(_appointment.id);
      attemptId = payment.id;
      if (!mounted) return;

      if (payment.approveUrl == null) {
        // Not silent: an attempt with no approval link is a server-side
        // problem the patient can do nothing about except try again, and
        // returning without a word would look like the button did nothing.
        await _abandonAttempt(attemptId);
        _showFailure(
          'Plaćanje nije pokrenuto.',
          'PayPal nam nije vratio stranicu za odobrenje plaćanja. Pokušajte ponovo za nekoliko trenutaka.',
          stillTrue: 'Ništa vam nije naplaćeno.',
        );
        return;
      }

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      if (approved != true) {
        await _abandonAttempt(attemptId);
        return;
      }

      await _paymentProvider.capture(payment.id);
      captured = true;
    } catch (e) {
      await _abandonAttempt(attemptId);
      // The reassurance is the important half here: a failed payment attempt
      // is the moment a patient most needs to know that no money moved.
      if (mounted) {
        _showFailure(
          'Plaćanje nije izvršeno.',
          e,
          stillTrue: 'Ništa vam nije naplaćeno i termin je i dalje zakazan.',
        );
      }
    } finally {
      if (mounted) setState(() => _isPaying = false);
    }

    if (!captured || !mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Plaćanje uspješno.')));

    // Re-fetch to pick up the server's fresh isPaid/paymentStatus. A failure
    // here means a stale screen, never a failed payment - saying otherwise
    // would tell a patient their money is safe moments after it was taken.
    try {
      final refreshed = await widget.provider.getById(_appointment.id);
      if (mounted) setState(() => _appointment = refreshed);
    } catch (e) {
      if (mounted) {
        _showFailure(
          'Plaćanje je evidentirano, ali prikaz nije osvježen.',
          e,
          stillTrue: 'Vratite se na listu termina da vidite status plaćanja.',
        );
      }
    }
  }

  /// Outcome first, then why, then what is still true - the shape every failure
  /// message in the app uses, so a patient never has to work out from a bare
  /// error string whether their appointment or their money survived it.
  /// [cause] is either a caught exception - translated by [failureCause] - or,
  /// where the screen diagnosed the problem itself and nothing was thrown, the
  /// finished sentence to show.
  void _showFailure(String outcome, Object cause, {String? stillTrue}) {
    final why = cause is String ? cause : failureCause(cause);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text([outcome, why, ?stillTrue].join(' ')),
        duration: const Duration(seconds: 6),
      ),
    );
  }

  /// Best-effort: the server retires a PayPal-refused attempt on its own and
  /// expires anything else shortly after, so a failure here costs a short wait
  /// rather than correctness. Harmless on an attempt that actually went
  /// through - the server refuses to retire a settled payment.
  Future<void> _abandonAttempt(int? paymentId) async {
    if (paymentId == null) return;
    await _paymentProvider.abandon(paymentId).catchError((_) {});
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
          // Money the clinic owes back is said first (review item C14) - being
          // told only "otkazan" while a failed refund goes unmentioned is
          // exactly the silence this item exists to remove.
          if (_appointment.refundFailed)
            const AppNotice(
              tone: AppTone.warning,
              message: 'Povrat sredstava nije uspio automatski. Klinika će ga izvršiti ručno u najkraćem roku.',
            )
          // Rulebook §J: once paid, the UI states it plainly and the pay button
          // is gone - never a second chance to pay the same thing twice.
          else if (_appointment.isPaid)
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
          if (_appointment.canReschedule) ...[
            const SizedBox(height: AppSpacing.lg),
            OutlinedButton.icon(
              onPressed: _reschedule,
              icon: const Icon(Icons.edit_calendar_outlined),
              label: const Text('Premjesti termin'),
            ),
          ],
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
