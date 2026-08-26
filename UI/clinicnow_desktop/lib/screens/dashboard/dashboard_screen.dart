import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';
import '../../models/dashboard_summary.dart';

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
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            const SizedBox(height: 12),
            FilledButton(onPressed: _load, child: const Text('Pokušaj ponovo')),
          ],
        ),
      );
    }

    final summary = _summary;
    if (summary == null) {
      return const Center(child: CircularProgressIndicator());
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Align(
            alignment: Alignment.centerRight,
            child: IconButton(
              tooltip: 'Osvježi',
              icon: _isLoading
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.refresh),
              onPressed: _isLoading ? null : _load,
            ),
          ),
          Wrap(
            spacing: 16,
            runSpacing: 16,
            children: [
              _KpiCard(label: 'Termini danas', value: '${summary.todayAppointmentsCount}', icon: Icons.event_outlined),
              _KpiCard(label: 'Aktivni pacijenti', value: '${summary.activePatientsCount}', icon: Icons.people_outline),
              _KpiCard(label: 'Dostupni doktori sada', value: '${summary.availableDoctorsCount}', icon: Icons.medical_services_outlined),
              _KpiCard(label: 'Prihod ovog mjeseca', value: '${summary.monthlyRevenueEur.toStringAsFixed(2)} EUR', icon: Icons.payments_outlined),
            ],
          ),
          const SizedBox(height: 24),
          Text('Termini u posljednjih 7 dana', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          SizedBox(height: 220, child: _WeeklyTrendChart(trend: summary.weeklyTrend, weekdayLabel: _weekdayLabel)),
          const SizedBox(height: 24),
          Text('Novi vs. postojeći pacijenti (30 dana)', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          SizedBox(
            height: 220,
            child: _NewVsExistingChart(newCount: summary.newPatientsCount30d, existingCount: summary.existingPatientsCount30d),
          ),
        ],
      ),
    );
  }

  static String _weekdayLabel(DateTime date) => _weekdayAbbrev[date.weekday - 1];
}

class _KpiCard extends StatelessWidget {
  final String label;
  final String value;
  final IconData icon;

  const _KpiCard({required this.label, required this.value, required this.icon});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 220,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Icon(icon, size: 32, color: Theme.of(context).colorScheme.primary),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(value, style: Theme.of(context).textTheme.headlineSmall),
                    Text(label, style: Theme.of(context).textTheme.bodySmall),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _WeeklyTrendChart extends StatelessWidget {
  final List<WeeklyTrendPoint> trend;
  final String Function(DateTime) weekdayLabel;

  const _WeeklyTrendChart({required this.trend, required this.weekdayLabel});

  @override
  Widget build(BuildContext context) {
    if (trend.isEmpty) return const Center(child: Text('Nema podataka.'));
    final maxCount = trend.map((t) => t.appointmentCount).fold<int>(0, (a, b) => a > b ? a : b);

    return BarChart(
      BarChartData(
        alignment: BarChartAlignment.spaceAround,
        maxY: (maxCount + 1).toDouble(),
        titlesData: FlTitlesData(
          leftTitles: const AxisTitles(sideTitles: SideTitles(showTitles: true, reservedSize: 28)),
          topTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          rightTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          bottomTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              getTitlesWidget: (value, meta) {
                final index = value.toInt();
                if (index < 0 || index >= trend.length) return const SizedBox.shrink();
                return Padding(padding: const EdgeInsets.only(top: 4), child: Text(weekdayLabel(trend[index].date)));
              },
            ),
          ),
        ),
        borderData: FlBorderData(show: false),
        gridData: const FlGridData(show: true, drawVerticalLine: false),
        barGroups: [
          for (var i = 0; i < trend.length; i++)
            BarChartGroupData(x: i, barRods: [
              BarChartRodData(toY: trend[i].appointmentCount.toDouble(), width: 18, color: Theme.of(context).colorScheme.primary),
            ]),
        ],
      ),
    );
  }
}

class _NewVsExistingChart extends StatelessWidget {
  final int newCount;
  final int existingCount;

  const _NewVsExistingChart({required this.newCount, required this.existingCount});

  @override
  Widget build(BuildContext context) {
    if (newCount == 0 && existingCount == 0) {
      return const Center(child: Text('Nema podataka.'));
    }
    return PieChart(
      PieChartData(
        sectionsSpace: 2,
        centerSpaceRadius: 36,
        sections: [
          PieChartSectionData(value: newCount.toDouble(), title: 'Novi\n$newCount', color: Theme.of(context).colorScheme.primary, radius: 60),
          PieChartSectionData(value: existingCount.toDouble(), title: 'Postojeći\n$existingCount', color: Theme.of(context).colorScheme.tertiary, radius: 60),
        ],
      ),
    );
  }
}
