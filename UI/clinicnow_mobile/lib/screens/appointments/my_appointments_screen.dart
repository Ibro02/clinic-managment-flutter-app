import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';
import 'appointment_detail_screen.dart';
import 'book_appointment_screen.dart';

/// One of the four statuses, or "all". Kept as a type rather than a nullable
/// `int` so the filter bar, the query and the empty-state copy all read from
/// the same list instead of three parallel switch statements (rulebook Part II
/// §D: no magic numbers).
enum _StatusFilter {
  all('Svi', null),
  pending('Na čekanju', 0),
  confirmed('Potvrđeni', 1),
  completed('Završeni', 2),
  cancelled('Otkazani', 3);

  const _StatusFilter(this.label, this.status);

  final String label;

  /// Matches .NET's `AppointmentStatus`; null means "don't filter".
  final int? status;
}

/// "My appointments" master-detail (PLAN.md Phase 4 item 6). The list is
/// already scoped server-side to the caller's own appointments (no `patientId`
/// query param needed - the backend resolves it from the JWT, rulebook §5), so
/// this screen only has to render whatever it gets back.
///
/// Upcoming and past are two separate server queries rather than one list split
/// in memory (review item C18). Two reasons: each half then gets the ordering
/// that actually helps - soonest first for what's coming, most recent first for
/// what's done - and neither can be crowded out of the page by the other, which
/// is what happens to a patient with fifty past visits when a single descending
/// page is split client-side.
class MyAppointmentsScreen extends StatefulWidget {
  const MyAppointmentsScreen({super.key});

  @override
  State<MyAppointmentsScreen> createState() => _MyAppointmentsScreenState();
}

class _MyAppointmentsScreenState extends State<MyAppointmentsScreen> {
  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  late final AppointmentProvider _provider;

  _StatusFilter _filter = _StatusFilter.all;

  /// Distinguished from [_isReloading] on purpose. The first load has nothing
  /// to show, so it gets a placeholder; every later load already has a correct
  /// list on screen, and replacing that with a spinner would flash a worse UI
  /// than the one it is refreshing.
  bool _isFirstLoad = true;
  bool _isReloading = false;

  String? _error;
  List<Appointment> _upcoming = [];
  List<Appointment> _past = [];

  /// Guards against a slow response for a filter the user has already moved on
  /// from landing after a faster later one and overwriting it.
  int _requestSequence = 0;

  @override
  void initState() {
    super.initState();
    _provider = AppointmentProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    final sequence = ++_requestSequence;
    setState(() {
      _isReloading = true;
      _error = null;
    });

    // One instant for both halves, so an appointment can never be missing from
    // both lists (or appear in both) because the two queries were built a few
    // milliseconds apart.
    final now = DateTime.now().toUtc().toIso8601String();
    final status = _filter.status;

    try {
      final results = await Future.wait([
        _provider.getPaged({
          'pageSize': 50,
          'orderBy': 'StartUtc',
          'sortDirection': 'asc',
          'fromUtc': now,
          'status': ?status,
        }),
        _provider.getPaged({
          'pageSize': 50,
          'orderBy': 'StartUtc',
          'sortDirection': 'desc',
          'toUtc': now,
          'status': ?status,
        }),
      ]);

      if (!mounted || sequence != _requestSequence) return;
      setState(() {
        _upcoming = results[0].resultList;
        _past = results[1].resultList;
      });
    } catch (e) {
      // Includes the non-ApiException case (server unreachable - wrong
      // host/port, no network), which must never be silently swallowed into an
      // empty list with no explanation (rulebook Part II: unhappy paths are
      // surfaced, not hidden). `failureCause` turns either kind into copy that
      // says what to do next.
      if (!mounted || sequence != _requestSequence) return;
      setState(() => _error = failureCause(e));
    } finally {
      if (mounted && sequence == _requestSequence) {
        setState(() {
          _isReloading = false;
          _isFirstLoad = false;
        });
      }
    }
  }

  void _applyFilter(_StatusFilter filter) {
    if (filter == _filter) return;
    setState(() => _filter = filter);
    _load();
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
    _load(); // refresh in case it was cancelled or paid in the detail screen
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
      body: Column(
        children: [
          _filterBar(context),
          // Reserved height, not a conditional widget: a progress bar that
          // appears and disappears would shove the whole list up and down by
          // two pixels on every filter change.
          SizedBox(
            height: 2,
            child: _isReloading && !_isFirstLoad ? const LinearProgressIndicator(minHeight: 2) : null,
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: _load,
              child: _content(context),
            ),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _openBooking,
        icon: const Icon(Icons.add),
        label: const Text('Zakaži termin'),
      ),
    );
  }

  Widget _filterBar(BuildContext context) {
    return SizedBox(
      height: 52,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md, vertical: AppSpacing.xs),
        itemCount: _StatusFilter.values.length,
        separatorBuilder: (context, index) => const SizedBox(width: AppSpacing.xs),
        itemBuilder: (context, index) {
          final option = _StatusFilter.values[index];
          return ChoiceChip(
            label: Text(option.label),
            selected: option == _filter,
            onSelected: (_) => _applyFilter(option),
          );
        },
      ),
    );
  }

  Widget _content(BuildContext context) {
    // Every branch is a scrollable, so pull-to-refresh keeps working on the
    // empty and error states too.
    final padding = const EdgeInsets.fromLTRB(
      AppSpacing.md,
      AppSpacing.xs,
      AppSpacing.md,
      // Clearance for the FAB, so the last card is never pinned underneath it.
      AppSpacing.xxl + AppSpacing.lg,
    );

    if (_error != null) {
      return ListView(
        padding: padding,
        children: [
          AppErrorState(
            title: 'Termini nisu učitani',
            message: _error!,
            onRetry: _load,
          ),
        ],
      );
    }

    if (_isFirstLoad) {
      return ListView(
        padding: padding,
        physics: const AlwaysScrollableScrollPhysics(),
        children: const [_LoadingPlaceholder()],
      );
    }

    if (_upcoming.isEmpty && _past.isEmpty) {
      return ListView(
        padding: padding,
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          _filter == _StatusFilter.all
              ? AppEmptyState(
                  icon: Icons.event_available_outlined,
                  title: 'Nemate zakazanih termina',
                  message: 'Kada zakažete termin, pojavit će se ovdje.',
                  action: FilledButton.icon(
                    onPressed: _openBooking,
                    icon: const Icon(Icons.add_rounded, size: 18),
                    label: const Text('Zakaži termin'),
                  ),
                )
              : AppEmptyState(
                  icon: Icons.filter_alt_off_outlined,
                  title: 'Nema termina u ovom statusu',
                  message:
                      'Nijedan vaš termin trenutno nije u statusu "${_filter.label.toLowerCase()}". '
                      'Odaberite "Svi" da vidite sve termine.',
                  action: OutlinedButton.icon(
                    onPressed: () => _applyFilter(_StatusFilter.all),
                    icon: const Icon(Icons.list_rounded, size: 18),
                    label: const Text('Prikaži sve'),
                  ),
                ),
        ],
      );
    }

    return ListView(
      padding: padding,
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        // A section with nothing in it is left out entirely rather than shown
        // with an "empty" line under it - a patient with no history should not
        // be told twice that they have no history.
        if (_upcoming.isNotEmpty) ..._section(context, 'Predstojeći', _upcoming),
        if (_upcoming.isNotEmpty && _past.isNotEmpty) const SizedBox(height: AppSpacing.md),
        if (_past.isNotEmpty) ..._section(context, 'Prethodni', _past),
      ],
    );
  }

  List<Widget> _section(BuildContext context, String title, List<Appointment> appointments) {
    return [
      Padding(
        padding: const EdgeInsets.fromLTRB(AppSpacing.xxs, AppSpacing.xs, 0, AppSpacing.xs),
        child: Row(
          children: [
            Text(title.toUpperCase(), style: context.text.labelSmall),
            const SizedBox(width: AppSpacing.xs),
            Text(
              '${appointments.length}',
              style: context.text.labelSmall?.copyWith(color: context.colors.textMuted),
            ),
          ],
        ),
      ),
      for (final appointment in appointments) ...[
        _card(appointment),
        const SizedBox(height: AppSpacing.xs),
      ],
    ];
  }

  Widget _card(Appointment appointment) {
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
  }
}

/// Neutral, layout-stable stand-in for the first load.
///
/// Deliberately not a full list of card-shaped skeletons: this screen does not
/// yet know whether the patient has any appointments at all, and a skeleton
/// that promises five rows to someone who has none is a worse first impression
/// than a plain "loading" line that is true in either outcome.
class _LoadingPlaceholder extends StatelessWidget {
  const _LoadingPlaceholder();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: AppSpacing.xxl),
      child: Column(
        children: [
          const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2)),
          const SizedBox(height: AppSpacing.md),
          Text(
            'Učitavamo vaše termine…',
            style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
          ),
        ],
      ),
    );
  }
}
