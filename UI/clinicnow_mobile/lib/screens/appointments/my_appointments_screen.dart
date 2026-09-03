import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';
import 'appointment_detail_screen.dart';
import 'book_appointment_screen.dart';

/// "My appointments" master-detail (PLAN.md Phase 4 item 6). The list is
/// already scoped server-side to the caller's own appointments (no `patientId`
/// query param needed - the backend resolves it from the JWT, rulebook §5), so
/// this screen only has to render whatever it gets back.
class MyAppointmentsScreen extends StatefulWidget {
  const MyAppointmentsScreen({super.key});

  @override
  State<MyAppointmentsScreen> createState() => _MyAppointmentsScreenState();
}

class _MyAppointmentsScreenState extends State<MyAppointmentsScreen> {
  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  late final AppointmentProvider _provider;
  bool _isLoading = true;
  String? _error;
  List<Appointment> _appointments = [];

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
      final result = await _provider.getPaged({'pageSize': 50, 'orderBy': 'StartUtc', 'sortDirection': 'desc'});
      if (!mounted) return;
      setState(() => _appointments = result.resultList);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } catch (e) {
      // A non-ApiException failure (e.g. the server unreachable - wrong host/port,
      // no network) must never be silently swallowed into an empty list with no
      // explanation (rulebook Part II: unhappy paths must be surfaced, not hidden).
      if (!mounted) return;
      setState(() => _error = 'Greška prilikom učitavanja termina: $e');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _openBooking() async {
    final booked = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => const BookAppointmentScreen()),
    );
    if (booked == true) _load();
  }

  Future<void> _openDetail(Appointment appointment) async {
    await Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => AppointmentDetailScreen(appointment: appointment, provider: _provider)),
    );
    _load(); // refresh in case it was cancelled in the detail screen
  }

  /// Status as a semantic tone. Shared vocabulary with the desktop app, so a
  /// cancelled appointment reads the same on both - and the token layer picks
  /// the pairing per theme rather than this screen hand-picking a colour that
  /// happens to be illegible in dark mode.
  AppTone _statusTone(int status) => switch (status) {
    0 => AppTone.warning, // Na čekanju
    1 => AppTone.info, // Potvrđen
    2 => AppTone.success, // Završen
    3 => AppTone.danger, // Otkazan
    _ => AppTone.neutral,
  };

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: RefreshIndicator(
        onRefresh: _load,
        child: _isLoading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
            ? ListView(
                padding: const EdgeInsets.all(AppSpacing.md),
                children: [AppErrorState(message: _error!, onRetry: _load)],
              )
            : _appointments.isEmpty
            // ListView (not Center) so pull-to-refresh still works on an empty list.
            ? ListView(
                padding: const EdgeInsets.all(AppSpacing.md),
                children: [
                  AppEmptyState(
                    icon: Icons.event_available_outlined,
                    title: 'Nemate zakazanih termina',
                    message: 'Kada zakažete termin, pojavit će se ovdje.',
                    action: FilledButton.icon(
                      onPressed: _openBooking,
                      icon: const Icon(Icons.add_rounded, size: 18),
                      label: const Text('Zakaži termin'),
                    ),
                  ),
                ],
              )
            : ListView.separated(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.md,
                  AppSpacing.md,
                  AppSpacing.md,
                  // Clearance for the FAB, so the last card is never pinned
                  // underneath it.
                  AppSpacing.xxl + AppSpacing.lg,
                ),
                itemCount: _appointments.length,
                separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
                itemBuilder: (context, index) {
                  final appointment = _appointments[index];
                  final tone = _statusTone(appointment.status);

                  return AppListCard(
                    icon: Icons.event_outlined,
                    tone: tone,
                    title: appointment.doctorName,
                    subtitle: appointment.medicalServiceName,
                    meta:
                        '${_dateTimeFormat.format(appointment.startUtc)} · '
                        '${appointment.locationName}',
                    badgeLabel: appointment.statusName,
                    badgeTone: tone,
                    onTap: () => _openDetail(appointment),
                  );
                },
              ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _openBooking,
        icon: const Icon(Icons.add),
        label: const Text('Zakaži termin'),
      ),
    );
  }
}
