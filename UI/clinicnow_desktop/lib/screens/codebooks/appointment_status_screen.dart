import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/appointment_status_info.dart';
import '../../providers/appointment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';

/// Read-only "Statusi termina" codebook tab (review item S2). The prijava and
/// the rulebook both list appointment statuses as reference data that needs a
/// management form, but they are a fixed part of the centralized state
/// machine (rulebook §7) - letting a user add or delete one would break the
/// transition guarantees the state machine exists to enforce. This screen is
/// the disabled-with-reason answer the rulebook §6 asks for: it shows exactly
/// what the state machine knows, with no Add/Edit/Delete affordance at all,
/// fetched live from the backend rather than hardcoded here.
class AppointmentStatusScreen extends StatefulWidget {
  const AppointmentStatusScreen({super.key});

  @override
  State<AppointmentStatusScreen> createState() => _AppointmentStatusScreenState();
}

class _AppointmentStatusScreenState extends State<AppointmentStatusScreen> {
  late final AppointmentProvider _provider;
  List<AppointmentStatusInfo> _statuses = [];
  bool _isLoading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = AppointmentProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final statuses = await _provider.statuses();
      if (!mounted) return;
      setState(() {
        _statuses = statuses;
        _isLoading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _isLoading = false;
      });
    }
  }

  AppTone _statusTone(int status) => switch (status) {
    0 => AppTone.warning, // Na čekanju
    1 => AppTone.info, // Potvrđen
    2 => AppTone.success, // Završen
    3 => AppTone.danger, // Otkazan
    _ => AppTone.neutral,
  };

  static const _actionLabels = {
    'Confirm': 'Potvrdi',
    'Complete': 'Završi',
    'Cancel': 'Otkaži',
    'Reschedule': 'Premjesti',
  };

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return Padding(
      padding: AppSpacing.page,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Container(
            padding: const EdgeInsets.all(AppSpacing.sm),
            decoration: BoxDecoration(
              color: c.infoSoft,
              borderRadius: AppRadius.all(AppRadius.md),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.info_outline, size: 18, color: c.info),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Text(
                    'Statusi su fiksni dio životnog ciklusa termina i mijenjaju se isključivo kroz '
                    'definisane prelaze u centralizovanoj state machine logici. Zbog toga se ne mogu '
                    'dodavati ni brisati - lista ispod je uvijek u skladu sa stvarnim pravilima backend-a.',
                    style: TextStyle(color: c.textSecondary, height: 1.4),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AppDataTable<AppointmentStatusInfo>(
              rows: _statuses,
              isLoading: _isLoading,
              error: _error,
              onRetry: _load,
              emptyTitle: 'Nema statusa',
              emptyMessage: 'Statusi termina nisu učitani.',
              columns: [
                AppColumn(
                  label: 'Status',
                  width: 160,
                  cell: (context, s) => AppStatusBadge(label: s.statusName, tone: _statusTone(s.status)),
                ),
                AppColumn(
                  label: 'Opis',
                  flex: 2,
                  cell: (context, s) => Text(s.description),
                ),
                AppColumn(
                  label: 'Dozvoljeni prelazi',
                  flex: 3,
                  cell: (context, s) => s.allowedActions.isEmpty
                      ? Text('— (terminalno)', style: TextStyle(color: c.textMuted))
                      : Wrap(
                          spacing: AppSpacing.xxs,
                          runSpacing: AppSpacing.xxs,
                          children: s.allowedActions
                              .map((a) => AppStatusBadge(label: _actionLabels[a] ?? a, tone: AppTone.primary, showDot: false))
                              .toList(),
                        ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
