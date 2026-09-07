import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../core/reports_api.dart';
import '../../layouts/shell_navigation.dart';
import '../../models/appointment.dart';
import '../../models/dashboard_summary.dart';
import '../../providers/appointment_provider.dart';
import '../../widgets/charts/app_bar_chart.dart';
import '../../widgets/charts/app_donut_chart.dart';
import '../../widgets/charts/chart_card.dart';
import '../../widgets/charts/chart_series.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_fields.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/stat_card.dart';

/// Landing screen for Administrator/Staff (Phase 9) - at-a-glance clinic KPIs,
/// charts, and today's queue.
///
/// Review item C8 closed three gaps here: the figures refresh on their own
/// rather than only when someone presses "Osvježi", each KPI card opens the
/// screen its number came from, and the next appointments are listed with the
/// actions staff would otherwise go to the Termini screen to perform.
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> with WidgetsBindingObserver {
  static const _weekdayAbbrev = ['Pon', 'Uto', 'Sri', 'Čet', 'Pet', 'Sub', 'Ned'];

  /// Longer than the notification poll: these are clinic-wide aggregates that
  /// move over minutes, and each tick is a summary query plus a page of
  /// appointments. Half a minute keeps "Termini danas" honest without making
  /// the dashboard the most expensive screen in the app.
  static const _refreshInterval = Duration(seconds: 30);

  /// How far ahead the queue looks, and how many rows it shows. A dashboard
  /// table is a prompt to act, not a replacement for the Termini screen - the
  /// "Svi termini" button goes there.
  static const _upcomingWindow = Duration(hours: 24);
  static const _upcomingLimit = 8;

  static final _timeFormat = DateFormat('dd.MM. HH:mm');

  late final ReportsApi _api;
  late final AppointmentProvider _appointments;

  Timer? _timer;
  bool _isForeground = true;

  DashboardSummary? _summary;
  List<Appointment> _upcoming = [];
  String? _error;
  bool _isLoading = false;

  /// Set while a row action is in flight, so its buttons disable instead of
  /// accepting a second click that would fail server-side.
  int? _busyAppointmentId;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    final session = context.read<AuthSession>();
    _api = ReportsApi(session);
    _appointments = AppointmentProvider(session);
    _load();
    _timer = Timer.periodic(_refreshInterval, (_) => _load());
  }

  @override
  void dispose() {
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final isForeground = state == AppLifecycleState.resumed;
    if (isForeground == _isForeground) return;
    _isForeground = isForeground;

    _timer?.cancel();
    if (!isForeground) return;

    // A minimised window doesn't need a 30-second tick, but the moment it comes
    // back the figures on screen are stale - refresh at once rather than
    // showing yesterday's number for another half minute.
    _load();
    _timer = Timer.periodic(_refreshInterval, (_) => _load());
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() {
      _isLoading = true;
      _error = null;
    });

    final now = DateTime.now().toUtc();
    try {
      // Started together, awaited separately: `Future.wait` over two different
      // result types would collapse them to Object and need a cast back.
      final summaryRequest = _api.getDashboardSummary();
      final upcomingRequest = _appointments.getPaged({
        'pageSize': _upcomingLimit,
        'orderBy': 'StartUtc',
        'sortDirection': 'asc',
        'fromUtc': now.toIso8601String(),
        'toUtc': now.add(_upcomingWindow).toIso8601String(),
      });

      final summary = await summaryRequest;
      final upcoming = await upcomingRequest;
      if (!mounted) return;
      setState(() {
        _summary = summary;
        _upcoming = upcoming.resultList;
      });
    } catch (e) {
      if (mounted) setState(() => _error = failureCause(e));
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _runAction(Appointment appointment, Future<Appointment> Function() action, String outcome) async {
    setState(() => _busyAppointmentId = appointment.id);
    try {
      await action();
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(outcome)));
      await _load();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Radnja nije izvršena. ${failureCause(e)} Termin je ostao nepromijenjen.'),
            duration: const Duration(seconds: 6),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _busyAppointmentId = null);
    }
  }

  Future<void> _confirm(Appointment appointment) => _runAction(
        appointment,
        () => _appointments.confirm(appointment.id),
        'Termin je potvrđen.',
      );

  Future<void> _complete(Appointment appointment) => _runAction(
        appointment,
        () => _appointments.complete(appointment.id),
        'Termin je označen kao završen.',
      );

  Future<void> _cancel(Appointment appointment) async {
    // Cancelling is irreversible and the backend requires a reason, so the
    // dashboard asks for one here rather than sending the user to another
    // screen to do it (rulebook Part II §G/§K).
    final reasonController = TextEditingController();
    String? reasonError;

    final reason = await showAppDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Otkazivanje termina',
          subtitle: '${appointment.patientName} · ${_timeFormat.format(appointment.startUtc.toLocal())}',
          icon: Icons.cancel_outlined,
          tone: AppTone.danger,
          width: 460,
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
            help: 'Razlog se šalje pacijentu uz obavijest o otkazivanju.',
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
    await _runAction(appointment, () => _appointments.cancel(appointment.id, reason), 'Termin je otkazan.');
  }

  @override
  Widget build(BuildContext context) {
    final summary = _summary;

    // Only when nothing has loaded yet. Once figures are on screen, a failed
    // background tick shows a notice above them instead of replacing correct
    // numbers with an error page nobody asked for.
    if (summary == null) {
      return Padding(
        padding: AppSpacing.page,
        child: _error != null
            ? AppErrorState(title: 'Pregled nije učitan', message: _error!, onRetry: _load)
            : const Center(child: CircularProgressIndicator()),
      );
    }

    final trend = summary.weeklyTrend;

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: AppSpacing.page,
        children: [
          AppToolbar(
            title: 'Pregled',
            subtitle: 'Stanje klinike danas. Osvježava se automatski.',
            actions: [
              OutlinedButton.icon(
                onPressed: _isLoading ? null : _load,
                icon: _isLoading
                    ? const SizedBox(height: 14, width: 14, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.refresh_rounded, size: 16),
                label: const Text('Osvježi'),
              ),
            ],
          ),
          if (_error != null) ...[
            AppNotice(
              tone: AppTone.warning,
              icon: Icons.sync_problem_rounded,
              message: 'Brojke možda nisu najnovije. $_error',
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          _kpiRow(summary),
          const SizedBox(height: AppSpacing.lg),
          _upcomingCard(),
          const SizedBox(height: AppSpacing.lg),
          ChartCard(
            title: 'Termini u posljednjih 7 dana',
            value: '${trend.fold<int>(0, (sum, t) => sum + t.appointmentCount)}',
            subtitle: 'Ukupno zakazanih termina u sedmici.',
            child: trend.isEmpty
                ? const AppEmptyState(
                    icon: Icons.bar_chart_rounded,
                    title: 'Nema podataka',
                    message: 'Za ovaj period nema zabilježenih termina.',
                  )
                : AppBarChart(
                    height: 240,
                    labels: [for (final point in trend) _weekdayLabel(point.date)],
                    series: [
                      AppChartSeries(
                        name: 'Termini',
                        values: [for (final point in trend) point.appointmentCount.toDouble()],
                      ),
                    ],
                  ),
          ),
          const SizedBox(height: AppSpacing.lg),
          ChartCard(
            title: 'Novi vs. postojeći pacijenti',
            subtitle: 'Posljednjih 30 dana.',
            child: (summary.newPatientsCount30d == 0 && summary.existingPatientsCount30d == 0)
                ? const AppEmptyState(
                    icon: Icons.donut_large_rounded,
                    title: 'Nema podataka',
                    message: 'U posljednjih 30 dana nije bilo pacijenata.',
                  )
                : Center(
                    child: AppDonutChart(
                      centerLabel: 'Pacijenti',
                      slices: [
                        AppChartSlice(name: 'Novi', value: summary.newPatientsCount30d.toDouble()),
                        AppChartSlice(name: 'Postojeći', value: summary.existingPatientsCount30d.toDouble()),
                      ],
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  /// The CRM KPI row. Cards size themselves to the window rather than sitting
  /// at a fixed 220px, so a wide monitor gets four even columns instead of four
  /// narrow tiles and a gap.
  ///
  /// Each card opens the screen its number came from (review item C8). A card
  /// whose destination this role cannot see stays a plain tile rather than a
  /// click that silently does nothing.
  Widget _kpiRow(DashboardSummary summary) {
    final navigation = context.read<ShellNavigation>();

    VoidCallback? open(ShellDestination destination) =>
        navigation.canOpen(destination) ? () => navigation.open(destination) : null;

    final cards = <Widget>[
      StatCard(
        label: 'Termini danas',
        value: '${summary.todayAppointmentsCount}',
        icon: Icons.event_outlined,
        caption: navigation.canOpen(ShellDestination.appointments) ? 'Otvori raspored' : null,
        onTap: open(ShellDestination.appointments),
      ),
      StatCard(
        label: 'Aktivni pacijenti',
        value: '${summary.activePatientsCount}',
        icon: Icons.people_outline,
        tone: AppTone.info,
        caption: navigation.canOpen(ShellDestination.patients) ? 'Otvori kartone' : null,
        onTap: open(ShellDestination.patients),
      ),
      StatCard(
        label: 'Dostupni doktori sada',
        value: '${summary.availableDoctorsCount}',
        icon: Icons.medical_services_outlined,
        tone: AppTone.success,
        caption: navigation.canOpen(ShellDestination.doctors) ? 'Otvori raspored doktora' : null,
        onTap: open(ShellDestination.doctors),
      ),
      StatCard(
        label: 'Prihod ovog mjeseca',
        value: '${summary.monthlyRevenueEur.toStringAsFixed(2)} EUR',
        icon: Icons.payments_outlined,
        tone: AppTone.warning,
        caption: navigation.canOpen(ShellDestination.reports) ? 'Otvori izvještaj o prihodima' : null,
        onTap: open(ShellDestination.reports),
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        const minCardWidth = 240.0;
        final perRow = (constraints.maxWidth / minCardWidth).floor().clamp(1, cards.length);
        final width = (constraints.maxWidth - (perRow - 1) * AppSpacing.md) / perRow;

        return Wrap(
          spacing: AppSpacing.md,
          runSpacing: AppSpacing.md,
          children: [for (final card in cards) SizedBox(width: width, child: card)],
        );
      },
    );
  }

  Widget _upcomingCard() {
    final navigation = context.read<ShellNavigation>();

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppToolbar(
            title: 'Sljedeći termini',
            subtitle: 'Narednih 24 sata. Potvrdite, završite ili otkažite bez napuštanja pregleda.',
            actions: [
              if (navigation.canOpen(ShellDestination.appointments))
                TextButton.icon(
                  onPressed: () => navigation.open(ShellDestination.appointments),
                  icon: const Icon(Icons.open_in_new_rounded, size: 16),
                  label: const Text('Svi termini'),
                ),
            ],
          ),
          AppDataTable<Appointment>(
            rows: _upcoming,
            expand: false,
            emptyTitle: 'Nema termina u narednih 24 sata',
            emptyMessage: 'Kada se zakaže termin za danas ili sutra, pojavit će se ovdje.',
            columns: [
              AppColumn<Appointment>(
                label: 'Vrijeme',
                width: 120,
                cell: (context, row) => Text(_timeFormat.format(row.startUtc.toLocal())),
              ),
              AppColumn<Appointment>(
                label: 'Pacijent',
                flex: 3,
                cell: (context, row) => Text(row.patientName, overflow: TextOverflow.ellipsis),
              ),
              AppColumn<Appointment>(
                label: 'Doktor',
                flex: 3,
                cell: (context, row) => Text(row.doctorName, overflow: TextOverflow.ellipsis),
              ),
              AppColumn<Appointment>(
                label: 'Usluga',
                flex: 3,
                cell: (context, row) => Text(row.medicalServiceName, overflow: TextOverflow.ellipsis),
              ),
              AppColumn<Appointment>(
                label: 'Status',
                width: 130,
                cell: (context, row) => AppStatusBadge(label: row.statusName, tone: _statusTone(row.status)),
              ),
            ],
            // Only the transitions the server says are legal right now
            // (`allowedActions`), so the dashboard never offers a button that
            // would come back as an error (rulebook Part II §K).
            rowActions: (context, row) {
              final isBusy = _busyAppointmentId == row.id;
              return [
                if (row.canConfirm)
                  AppRowAction(
                    icon: Icons.check_circle_outline_rounded,
                    tooltip: 'Potvrdi termin',
                    onPressed: isBusy ? null : () => _confirm(row),
                  ),
                if (row.canComplete)
                  AppRowAction(
                    icon: Icons.task_alt_rounded,
                    tooltip: 'Označi kao završen',
                    onPressed: isBusy ? null : () => _complete(row),
                  ),
                if (row.canCancel)
                  AppRowAction(
                    icon: Icons.cancel_outlined,
                    tooltip: 'Otkaži termin',
                    destructive: true,
                    onPressed: isBusy ? null : () => _cancel(row),
                  ),
              ];
            },
          ),
        ],
      ),
    );
  }

  /// Same status vocabulary as the appointments screen and the mobile app, so
  /// one appointment never reads as two different things.
  AppTone _statusTone(int status) => switch (status) {
    0 => AppTone.warning, // Na čekanju
    1 => AppTone.info, // Potvrđen
    2 => AppTone.success, // Završen
    3 => AppTone.danger, // Otkazan
    _ => AppTone.neutral,
  };

  static String _weekdayLabel(DateTime date) => _weekdayAbbrev[date.weekday - 1];
}
