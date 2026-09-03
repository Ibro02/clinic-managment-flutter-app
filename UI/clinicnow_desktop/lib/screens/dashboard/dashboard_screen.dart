import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/reports_api.dart';
import '../../models/dashboard_summary.dart';
import '../../widgets/charts/app_bar_chart.dart';
import '../../widgets/charts/app_donut_chart.dart';
import '../../widgets/charts/chart_card.dart';
import '../../widgets/charts/chart_series.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_fields.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/stat_card.dart';

/// Landing screen for Administrator/Staff (Phase 9) - at-a-glance clinic KPIs
/// + two charts, replacing the "Početna" placeholder that has been in
/// `app_shell.dart` since Phase 0.
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  static const _weekdayAbbrev = ['Pon', 'Uto', 'Sri', 'Čet', 'Pet', 'Sub', 'Ned'];

  late final ReportsApi _api;

  DashboardSummary? _summary;
  String? _error;
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _api = ReportsApi(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final summary = await _api.getDashboardSummary();
      if (mounted) setState(() => _summary = summary);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Padding(
        padding: AppSpacing.page,
        child: AppErrorState(message: _error!, onRetry: _load),
      );
    }

    final summary = _summary;
    if (summary == null) {
      return const Center(child: CircularProgressIndicator());
    }

    final trend = summary.weeklyTrend;

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: AppSpacing.page,
        children: [
          AppToolbar(
            title: 'Pregled',
            subtitle: 'Stanje klinike danas.',
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
          _kpiRow(summary),
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
  Widget _kpiRow(DashboardSummary summary) {
    final cards = <Widget>[
      StatCard(
        label: 'Termini danas',
        value: '${summary.todayAppointmentsCount}',
        icon: Icons.event_outlined,
      ),
      StatCard(
        label: 'Aktivni pacijenti',
        value: '${summary.activePatientsCount}',
        icon: Icons.people_outline,
        tone: AppTone.info,
      ),
      StatCard(
        label: 'Dostupni doktori sada',
        value: '${summary.availableDoctorsCount}',
        icon: Icons.medical_services_outlined,
        tone: AppTone.success,
      ),
      StatCard(
        label: 'Prihod ovog mjeseca',
        value: '${summary.monthlyRevenueEur.toStringAsFixed(2)} EUR',
        icon: Icons.payments_outlined,
        tone: AppTone.warning,
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

  static String _weekdayLabel(DateTime date) => _weekdayAbbrev[date.weekday - 1];
}
