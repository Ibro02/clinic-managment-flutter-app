import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/recommendation.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../core/design_tokens.dart';
import '../../providers/recommendation_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';
import '../appointments/book_appointment_screen.dart';

/// "Preporuke" tab (PLAN.md Phase 7 item 4): the explainable, content-based
/// suggestion list (recommender-dokumentacija.md §6 - every card carries a
/// real `Reason`, never a generic "recommended for you"), plus a real
/// doctor/service search box - the genuine source of `InteractionType.search`
/// signals (doc §3), not a synthetic one.
class RecommendationsScreen extends StatefulWidget {
  const RecommendationsScreen({super.key});

  @override
  State<RecommendationsScreen> createState() => _RecommendationsScreenState();
}

class _RecommendationsScreenState extends State<RecommendationsScreen> {
  late final RecommendationProvider _recommendationProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;
  final _dateFormat = DateFormat('EEEE, dd.MM.yyyy HH:mm');
  final _searchController = TextEditingController();

  List<AppointmentRecommendation>? _recommendations;
  String? _error;

  bool _isSearching = false;
  List<Doctor> _doctorResults = [];
  List<MedicalService> _serviceResults = [];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _recommendationProvider = RecommendationProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _load();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final items = await _recommendationProvider.getRecommendations();
      if (mounted) setState(() => _recommendations = items);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _search(String query) async {
    if (query.trim().isEmpty) {
      setState(() {
        _doctorResults = [];
        _serviceResults = [];
      });
      return;
    }

    setState(() => _isSearching = true);
    try {
      final doctors = await _doctorProvider.getPaged({'name': query, 'pageSize': 10});
      final services = await _serviceProvider.getPaged({'name': query, 'pageSize': 10});
      if (!mounted) return;
      setState(() {
        _doctorResults = doctors.resultList;
        _serviceResults = services.resultList;
      });

      // A real search interaction - genuinely written by a real user action,
      // not synthesized. Best-effort: a failed log must never block search.
      try {
        await _recommendationProvider.logInteraction(
          type: InteractionType.search,
          doctorId: doctors.resultList.isNotEmpty ? doctors.resultList.first.id : null,
          medicalServiceId: services.resultList.isNotEmpty ? services.resultList.first.id : null,
        );
      } catch (_) {
        // best-effort logging - never surfaces to the user
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isSearching = false);
    }
  }

  Future<void> _openBookingFor({Doctor? doctor, MedicalService? service}) async {
    if (doctor != null) {
      try {
        await _recommendationProvider.logInteraction(type: InteractionType.doctorView, doctorId: doctor.id);
      } catch (_) {}
    }
    if (service != null) {
      try {
        await _recommendationProvider.logInteraction(type: InteractionType.medicalServiceView, medicalServiceId: service.id);
      } catch (_) {}
    }

    if (!mounted) return;
    final booked = await Navigator.of(context).push<bool>(MaterialPageRoute(
      builder: (_) => BookAppointmentScreen(initialDoctorId: doctor?.id, initialMedicalServiceId: service?.id),
    ));
    if (booked == true) _load();
  }

  @override
  Widget build(BuildContext context) {
    final hasSearchResults = _doctorResults.isNotEmpty || _serviceResults.isNotEmpty;

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          const AppGreetingHeader(
            greeting: 'Pronađite njegu',
            subtitle: 'Pretražite doktore i usluge ili pogledajte prijedloge za vas.',
          ),
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              hintText: 'Pretražite doktore i usluge',
              prefixIcon: const Icon(Icons.search_rounded, size: 20),
              suffixIcon: _isSearching
                  ? const Padding(
                      padding: EdgeInsets.all(AppSpacing.sm),
                      child: SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
                    )
                  : null,
            ),
            onSubmitted: _search,
          ),
          if (hasSearchResults) ...[
            const SizedBox(height: AppSpacing.md),
            const AppSectionHeader(label: 'Rezultati pretrage'),
            for (final d in _doctorResults) ...[
              AppListCard(
                icon: Icons.medical_services_outlined,
                title: d.fullName,
                subtitle: d.specializations.isEmpty ? 'Bez specijalizacije' : d.specializations.join(', '),
                onTap: () => _openBookingFor(doctor: d),
              ),
              const SizedBox(height: AppSpacing.xs),
            ],
            for (final s in _serviceResults) ...[
              AppListCard(
                icon: Icons.event_note_outlined,
                tone: AppTone.info,
                title: s.name,
                subtitle: '${s.durationMinutes} min · ${s.price.toStringAsFixed(2)} KM',
                onTap: () => _openBookingFor(service: s),
              ),
              const SizedBox(height: AppSpacing.xs),
            ],
          ],
          const SizedBox(height: AppSpacing.sm),
          const AppSectionHeader(label: 'Preporučeno za vas'),
          if (_error != null)
            AppErrorState(message: _error!, onRetry: _load)
          else if (_recommendations == null)
            const Center(
              child: Padding(padding: EdgeInsets.all(AppSpacing.lg), child: CircularProgressIndicator()),
            )
          else if (_recommendations!.isEmpty)
            const AppEmptyState(
              icon: Icons.auto_awesome_outlined,
              title: 'Još nema preporuka',
              message: 'Zakažite prvi termin da bismo mogli personalizovati prijedloge.',
            )
          else
            for (final r in _recommendations!) ...[
              AppListCard(
                icon: Icons.auto_awesome_outlined,
                title: '${r.doctorName} — ${r.medicalServiceName}',
                subtitle: '${r.locationName} · ${_dateFormat.format(r.suggestedStartUtc.toLocal())}',
                // Rulebook §I: every suggestion carries its own reason, so the
                // patient sees why this was proposed rather than being handed
                // an unexplained pick.
                meta: r.reason,
                onTap: () => Navigator.of(context)
                    .push<bool>(
                      MaterialPageRoute(
                        builder: (_) => BookAppointmentScreen(
                          initialDoctorId: r.doctorId,
                          initialMedicalServiceId: r.medicalServiceId,
                          initialDate: r.suggestedStartUtc.toLocal(),
                        ),
                      ),
                    )
                    .then((booked) {
                      if (booked == true) _load();
                    }),
              ),
              const SizedBox(height: AppSpacing.xs),
            ],
        ],
      ),
    );
  }
}
