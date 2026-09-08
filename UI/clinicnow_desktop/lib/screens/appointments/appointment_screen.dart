import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../core/design_tokens.dart';
import '../../core/roles.dart';
import '../../models/appointment.dart';
import '../../models/doctor.dart';
import '../../models/patient.dart';
import '../../models/payment.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/patient_provider.dart';
import '../../providers/payment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_fields.dart';
import '../../widgets/ui/app_states.dart';
import 'lab_findings_screen.dart';
import 'referrals_screen.dart';
import 'reschedule_appointment_dialog.dart';
import 'schedule_appointment_dialog.dart';

/// Staff/doctor appointment management: list with ≥1 search param (patient AND
/// doctor filters, plus status), and the full Confirm/Complete/Cancel lifecycle
/// with confirmation dialogs - buttons for actions the current status doesn't
/// allow are simply not rendered, driven entirely by the server's own
/// `allowedActions` (PLAN.md Phase 4 item 5, rulebook Part II §K).
class AppointmentScreen extends StatefulWidget {
  const AppointmentScreen({super.key});

  @override
  State<AppointmentScreen> createState() => _AppointmentScreenState();
}

class _AppointmentScreenState extends State<AppointmentScreen> {
  static const int _pageSize = 10;
  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  late final AppointmentProvider _appointmentProvider;
  late final PatientProvider _patientProvider;
  late final DoctorProvider _doctorProvider;
  late final PaymentProvider _paymentProvider;

  int _page = 1;
  bool _isLoading = true;
  String? _error;
  List<Appointment> _appointments = [];
  int _count = 0;

  int? _filterPatientId;
  int? _filterDoctorId;
  int? _filterStatus;

  List<Patient> _patients = [];
  List<Doctor> _doctors = [];

  static const _statusOptions = [(0, 'Na čekanju'), (1, 'Potvrđen'), (2, 'Završen'), (3, 'Otkazan')];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _appointmentProvider = AppointmentProvider(authSession);
    _patientProvider = PatientProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _paymentProvider = PaymentProvider(authSession);
    _loadFilterOptions();
    _load();
  }

  Future<void> _loadFilterOptions() async {
    final patients = await _patientProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
    final doctors = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'User.LastName'});
    if (!mounted) return;
    setState(() {
      _patients = patients.resultList;
      _doctors = doctors.resultList;
    });
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final search = <String, dynamic>{
        'page': _page,
        'pageSize': _pageSize,
        'orderBy': 'StartUtc',
        'sortDirection': 'desc',
        if (_filterPatientId != null) 'patientId': _filterPatientId,
        if (_filterDoctorId != null) 'doctorId': _filterDoctorId,
        if (_filterStatus != null) 'status': _filterStatus,
      };
      final result = await _appointmentProvider.getPaged(search);
      if (!mounted) return;
      setState(() {
        _appointments = result.resultList;
        _count = result.count;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  void _resetPageAndLoad() {
    _page = 1;
    _load();
  }

  Future<void> _openScheduleDialog() async {
    final created = await showDialog<bool>(
      context: context,
      builder: (_) => const ScheduleAppointmentDialog(),
    );
    if (created == true) _load();
  }

  Future<void> _confirm(Appointment appointment) async {
    final ok = await _confirmDialog(
      title: 'Potvrda termina',
      message:
          'Potvrditi termin za ${appointment.patientName} kod ${appointment.doctorName} (${_dateTimeFormat.format(appointment.startUtc)})?',
      actionLabel: 'Potvrdi',
    );
    if (ok != true) return;

    try {
      await _appointmentProvider.confirm(appointment.id);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<void> _complete(Appointment appointment) async {
    final ok = await _confirmDialog(
      title: 'Završetak termina',
      message: 'Označiti termin za ${appointment.patientName} kao završen?',
      actionLabel: 'Završi',
    );
    if (ok != true) return;

    try {
      await _appointmentProvider.complete(appointment.id);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<void> _cancel(Appointment appointment) async {
    final reasonController = TextEditingController();
    String? reasonError;

    final reason = await showAppDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Otkazivanje termina',
          subtitle:
              '${appointment.patientName} kod ${appointment.doctorName} — '
              '${_dateTimeFormat.format(appointment.startUtc)}',
          icon: Icons.cancel_outlined,
          tone: AppTone.danger,
          width: 480,
          actions: [
            OutlinedButton(onPressed: () => Navigator.of(dialogContext).pop(), child: const Text('Odustani')),
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
          // Rulebook §G: a cancellation must carry a reason - it is recorded in
          // the audit trail and sent to the patient in the notification.
          child: AppField(
            label: 'Razlog otkazivanja',
            required: true,
            help: 'Pacijent vidi ovaj razlog u obavijesti o otkazivanju.',
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

    try {
      await _appointmentProvider.cancel(appointment.id, reason);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  /// "Historija termina" (review item 11/rulebook §7): the audit trail is
  /// written correctly on every status transition but was never surfaced
  /// anywhere - `AppointmentDto.AuditLogs` is only populated by the detail
  /// endpoint, so it is fetched here rather than read off the row already in
  /// [_appointments] (which came from the paged list, where it is always empty).
  Future<void> _openHistory(Appointment appointment) async {
    final Appointment detail;
    try {
      detail = await _appointmentProvider.getById(appointment.id);
    } on ApiException catch (e) {
      _showError(e.message);
      return;
    }
    if (!mounted) return;

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => AppDialog(
        title: 'Historija termina',
        subtitle: '${appointment.patientName} kod ${appointment.doctorName}',
        icon: Icons.history_rounded,
        width: 480,
        actions: [
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Zatvori'),
          ),
        ],
        child: detail.auditLogs.isEmpty
            ? Text(
                'Za ovaj termin još nema evidentiranih promjena statusa.',
                style: TextStyle(color: dialogContext.colors.textMuted),
              )
            : Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final entry in detail.auditLogs)
                    Padding(
                      padding: const EdgeInsets.only(bottom: AppSpacing.sm),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Padding(
                            padding: const EdgeInsets.only(top: 2),
                            child: AppStatusBadge(
                              label: entry.statusName,
                              tone: _statusToneByName(entry.statusName),
                            ),
                          ),
                          const SizedBox(width: AppSpacing.sm),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Text(
                                  '${entry.actingUserName} · ${_dateTimeFormat.format(entry.occurredAtUtc)}',
                                  style: dialogContext.text.bodySmall?.copyWith(
                                    color: dialogContext.colors.textMuted,
                                  ),
                                ),
                                if (entry.description != null && entry.description!.isNotEmpty)
                                  Text(entry.description!),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
      ),
    );
  }

  Future<void> _openLabFindings(Appointment appointment) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => LabFindingsScreen(
          patientId: appointment.patientId,
          patientName: appointment.patientName,
          initialAppointment: appointment,
        ),
      ),
    );
  }

  Future<void> _openReferrals(Appointment appointment) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => ReferralsScreen(
          patientId: appointment.patientId,
          patientName: appointment.patientName,
          initialAppointment: appointment,
        ),
      ),
    );
  }

  Future<void> _reschedule(Appointment appointment) async {
    final moved = await showDialog<bool>(
      context: context,
      builder: (_) => RescheduleAppointmentDialog(appointment: appointment),
    );
    if (moved == true) _load();
  }

  Future<void> _refund(Appointment appointment) async {
    if (appointment.paymentId == null) return;

    // The remaining refundable balance isn't on the Appointment model itself
    // (only Payment carries AmountEur/refund totals), so it's fetched from the
    // server before the dialog opens - the amount field is pre-filled with the
    // real remaining balance and the maximum is shown, instead of leaving staff
    // to guess and learn the real number from a 400 (design doc §7).
    final Payment payment;
    try {
      payment = await _paymentProvider.getByAppointmentId(appointment.id);
    } on ApiException catch (e) {
      _showError(e.message);
      return;
    }
    if (!mounted) return;

    final amountController = TextEditingController(text: payment.remainingRefundableEur.toStringAsFixed(2));
    final reasonController = TextEditingController();
    String? amountError;
    String? reasonError;

    final confirmed = await showAppDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Povrat sredstava',
          subtitle: '${appointment.patientName} kod ${appointment.doctorName}',
          icon: Icons.undo_rounded,
          tone: AppTone.warning,
          width: 500,
          actions: [
            OutlinedButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: () {
                final amount = double.tryParse(amountController.text.replaceAll(',', '.'));
                setDialogState(() {
                  amountError = (amount == null || amount <= 0)
                      ? 'Unesite ispravan iznos veći od 0.'
                      : (amount > payment.remainingRefundableEur
                            ? 'Iznos ne može biti veći od preostalih ${payment.remainingRefundableEur.toStringAsFixed(2)} EUR.'
                            : null);
                  reasonError = reasonController.text.trim().isEmpty ? 'Razlog povrata je obavezan.' : null;
                });
                if (amountError == null && reasonError == null) {
                  Navigator.of(dialogContext).pop(true);
                }
              },
              child: const Text('Izvrši povrat'),
            ),
          ],
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              // The remaining refundable balance is the number staff actually
              // need, so it is stated plainly rather than left to be inferred
              // from a 400 after submitting too much.
              AppNotice(
                tone: AppTone.info,
                message:
                    'Uplaćeno ${payment.amountEur.toStringAsFixed(2)} EUR · '
                    'već vraćeno ${payment.refundedAmountEur.toStringAsFixed(2)} EUR · '
                    'preostalo za povrat ${payment.remainingRefundableEur.toStringAsFixed(2)} EUR.',
              ),
              const SizedBox(height: AppSpacing.md),
              AppFormSection(
                children: [
                  AppField(
                    label: 'Iznos povrata (EUR)',
                    required: true,
                    help: 'Najviše ${payment.remainingRefundableEur.toStringAsFixed(2)} EUR.',
                    child: TextField(
                      controller: amountController,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      decoration: InputDecoration(errorText: amountError),
                    ),
                  ),
                  AppField(
                    label: 'Razlog povrata',
                    required: true,
                    child: TextField(
                      controller: reasonController,
                      decoration: InputDecoration(errorText: reasonError),
                      maxLines: 3,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );

    if (confirmed != true) return;

    try {
      final amount = double.parse(amountController.text.replaceAll(',', '.'));
      await _paymentProvider.refund(payment.id, amount, reasonController.text.trim());
      await _load();
      _showError(
        'Povrat je uspješno izvršen.',
      ); // reused SnackBar helper - message just happens to be a success, not an error
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<bool?> _confirmDialog({
    required String title,
    required String message,
    required String actionLabel,
  }) {
    return showConfirmDialog(context: context, title: title, message: message, confirmLabel: actionLabel);
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  /// Status as a semantic tone, not a colour. The token layer resolves the tone
  /// per theme, so a badge stays legible in dark mode without this screen
  /// knowing anything about foreground/background pairing - which is exactly
  /// what the previous hand-picked `Colors.orange`/`white` mapping kept getting
  /// wrong.
  AppTone _statusTone(int status) => switch (status) {
    0 => AppTone.warning, // Na čekanju
    1 => AppTone.info, // Potvrđen
    2 => AppTone.success, // Završen
    3 => AppTone.danger, // Otkazan
    _ => AppTone.neutral,
  };

  /// Same mapping as [_statusTone], keyed by the display name instead of the
  /// numeric code - `AppointmentAuditLogDto` only carries `StatusName`
  /// (rulebook §7's audit trail is display text, not a status the UI re-drives
  /// any logic from).
  AppTone _statusToneByName(String statusName) => switch (statusName) {
    'Na čekanju' => AppTone.warning,
    'Potvrđen' => AppTone.info,
    'Završen' => AppTone.success,
    'Otkazan' => AppTone.danger,
    _ => AppTone.neutral,
  };

  /// Backend `PaymentStatusExtensions.ToDisplayName(...)` values - the
  /// appointment DTO carries the payment status only as its display name, so
  /// these are the values the tone mapping below has to recognise by text.
  static const _partiallyRefundedLabel = 'Djelomično vraćeno';
  static const _requiresReconciliationLabel = 'Neusklađen iznos';

  /// Neutral when the appointment has no payment at all, success while it is
  /// fully paid, warning once part of it has been refunded, danger when it has
  /// been refunded in full - `isPaid` is false again in that last case,
  /// matching the backend's own `AppointmentDto.IsPaid` definition.
  ///
  /// A payment PayPal captured for the wrong amount (review item C13a) is paid
  /// as far as `isPaid` goes - the money did move - but it must never read as a
  /// clean green success, because someone has to reconcile it.
  AppTone _paymentTone(Appointment appointment) {
    if (appointment.paymentStatus == null) return AppTone.neutral;
    if (appointment.paymentStatus == _requiresReconciliationLabel) return AppTone.danger;
    if (!appointment.isPaid) return AppTone.danger;
    return appointment.paymentStatus == _partiallyRefundedLabel ? AppTone.warning : AppTone.success;
  }

  @override
  Widget build(BuildContext context) {
    // Backend only allows Administrator/Staff/Patient to schedule an
    // appointment (a Doctor manages their existing schedule via
    // Confirm/Complete/Cancel, but never books new ones from the desktop app)
    // - the button must not be shown to a role that would get a 403 on submit.
    final authSession = context.watch<AuthSession>();
    final canSchedule = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);
    // Payments aren't part of the doctor-facing surface (the backend rejects
    // a Doctor's refund attempt outright) - hiding the button avoids a
    // guaranteed-to-fail action instead of surfacing the rejection.
    final canRefundPayments = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    return Padding(
      padding: AppSpacing.page,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppToolbar(
            title: 'Termini',
            subtitle: 'Zakazivanje, potvrda i naplata termina.',
            actions: [
              OutlinedButton.icon(
                onPressed: _isLoading ? null : _load,
                icon: _isLoading
                    ? const SizedBox(height: 14, width: 14, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.refresh_rounded, size: 16),
                label: const Text('Osvježi'),
              ),
              if (canSchedule)
                FilledButton.icon(
                  onPressed: _openScheduleDialog,
                  icon: const Icon(Icons.add_rounded, size: 18),
                  label: const Text('Novi termin'),
                ),
            ],
          ),
          _filters(context),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AppDataTable<Appointment>(
              rows: _appointments,
              isLoading: _isLoading,
              error: _error,
              onRetry: _load,
              emptyTitle: 'Nema termina',
              emptyMessage: 'Nijedan termin ne odgovara odabranim filterima.',
              paging: AppTablePaging(page: _page - 1, pageSize: _pageSize, totalCount: _count),
              onPageChanged: (zeroBased) {
                setState(() => _page = zeroBased + 1);
                _load();
              },
              columns: [
                AppColumn(
                  label: 'Datum i vrijeme',
                  width: 160,
                  numeric: true,
                  cell: (context, a) => Text(_dateTimeFormat.format(a.startUtc)),
                ),
                AppColumn(
                  label: 'Pacijent',
                  flex: 2,
                  cell: (context, a) => Text(a.patientName, overflow: TextOverflow.ellipsis),
                ),
                AppColumn(
                  label: 'Doktor',
                  flex: 2,
                  cell: (context, a) => Text(a.doctorName, overflow: TextOverflow.ellipsis),
                ),
                AppColumn(
                  label: 'Usluga',
                  flex: 2,
                  cell: (context, a) => Text(a.medicalServiceName, overflow: TextOverflow.ellipsis),
                ),
                AppColumn(
                  label: 'Lokacija',
                  cell: (context, a) => Text(a.locationName, overflow: TextOverflow.ellipsis),
                ),
                AppColumn(
                  label: 'Status',
                  width: 130,
                  cell: (context, a) => AppStatusBadge(label: a.statusName, tone: _statusTone(a.status)),
                ),
                AppColumn(
                  label: 'Plaćanje',
                  width: 150,
                  // A refund the clinic still owes outranks the payment status
                  // here (review item C14): it is the one state on this row
                  // that needs somebody to actually do something.
                  cell: (context, a) => a.refundFailed
                      ? const AppStatusBadge(label: 'Povrat nije uspio', tone: AppTone.danger)
                      : AppStatusBadge(label: a.paymentStatus ?? 'Nije plaćeno', tone: _paymentTone(a)),
                ),
              ],
              // Rulebook §K: an action that is not currently legal stays visible
              // but disabled, with a tooltip saying why - never silently absent.
              rowActions: (context, a) => [
                AppRowAction(
                  icon: Icons.check_circle_outline,
                  tooltip: a.canConfirm ? 'Potvrdi' : 'Potvrda nije moguća u ovom statusu',
                  onPressed: a.canConfirm ? () => _confirm(a) : null,
                ),
                AppRowAction(
                  icon: Icons.task_alt,
                  tooltip: a.canComplete ? 'Završi' : 'Završetak nije moguć u ovom statusu',
                  onPressed: a.canComplete ? () => _complete(a) : null,
                ),
                AppRowAction(
                  icon: Icons.cancel_outlined,
                  tooltip: a.canCancel ? 'Otkaži' : 'Otkazivanje nije moguće u ovom statusu',
                  destructive: true,
                  onPressed: a.canCancel ? () => _cancel(a) : null,
                ),
                AppRowAction(
                  icon: Icons.edit_calendar_outlined,
                  tooltip: a.canReschedule ? 'Premjesti' : 'Premještanje nije moguće u ovom statusu',
                  onPressed: a.canReschedule ? () => _reschedule(a) : null,
                ),
                if (a.canRefund && canRefundPayments)
                  AppRowAction(
                    icon: Icons.undo_rounded,
                    tooltip: 'Povrat sredstava',
                    onPressed: () => _refund(a),
                  ),
                AppRowAction(
                  icon: Icons.biotech_outlined,
                  tooltip: 'Laboratorijski nalazi',
                  onPressed: () => _openLabFindings(a),
                ),
                AppRowAction(
                  icon: Icons.assignment_outlined,
                  tooltip: 'Uputnice',
                  onPressed: () => _openReferrals(a),
                ),
                AppRowAction(
                  icon: Icons.history_rounded,
                  tooltip: 'Historija termina',
                  onPressed: () => _openHistory(a),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Filter bar. Sits directly above the grid, matching every other list screen
  /// in the app.
  Widget _filters(BuildContext context) {
    final c = context.colors;

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: [
        SizedBox(
          width: 220,
          child: DropdownButtonFormField<int?>(
            initialValue: _filterPatientId,
            decoration: const InputDecoration(isDense: true),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi pacijenti')),
              ..._patients.map((p) => DropdownMenuItem(value: p.id, child: Text(p.fullName))),
            ],
            onChanged: (value) {
              _filterPatientId = value;
              _resetPageAndLoad();
            },
          ),
        ),
        SizedBox(
          width: 240,
          child: DropdownButtonFormField<int?>(
            initialValue: _filterDoctorId,
            decoration: const InputDecoration(isDense: true),
            itemHeight: null, // items are two lines (name + clinic)
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi doktori')),
              ..._doctors.map(
                (d) => DropdownMenuItem(
                  value: d.id,
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: AppSpacing.xxs),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(d.fullName),
                        Text(
                          d.locationName,
                          style: context.text.bodySmall?.copyWith(
                            color: clinicColor(d.locationId, Theme.of(context).brightness),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
            onChanged: (value) {
              _filterDoctorId = value;
              _resetPageAndLoad();
            },
          ),
        ),
        SizedBox(
          width: 180,
          child: DropdownButtonFormField<int?>(
            initialValue: _filterStatus,
            decoration: const InputDecoration(isDense: true),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ..._statusOptions.map((s) => DropdownMenuItem(value: s.$1, child: Text(s.$2))),
            ],
            onChanged: (value) {
              _filterStatus = value;
              _resetPageAndLoad();
            },
          ),
        ),
        if (_filterPatientId != null || _filterDoctorId != null || _filterStatus != null)
          SizedBox(
            height: AppSizes.controlHeight,
            child: TextButton.icon(
              onPressed: () {
                _filterPatientId = null;
                _filterDoctorId = null;
                _filterStatus = null;
                _resetPageAndLoad();
              },
              icon: Icon(Icons.filter_alt_off_outlined, size: 16, color: c.textSecondary),
              label: Text('Poništi filtere', style: context.text.labelMedium),
            ),
          ),
      ],
    );
  }
}
