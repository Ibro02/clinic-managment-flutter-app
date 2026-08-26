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
import '../../providers/recommendation_provider.dart';
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
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(12),
        children: [
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              border: const OutlineInputBorder(),
              labelText: 'Pretražite doktore i usluge',
              suffixIcon: _isSearching
                  ? const Padding(padding: EdgeInsets.all(12), child: SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)))
                  : const Icon(Icons.search),
            ),
            onSubmitted: _search,
          ),
          if (_doctorResults.isNotEmpty || _serviceResults.isNotEmpty) ...[
            const SizedBox(height: 12),
            ..._doctorResults.map((d) => Card(
                  child: ListTile(
                    leading: const Icon(Icons.medical_services_outlined),
                    title: Text(d.fullName),
                    subtitle: Text(d.specializations.join(', ')),
                    onTap: () => _openBookingFor(doctor: d),
                  ),
                )),
            ..._serviceResults.map((s) => Card(
                  child: ListTile(
                    leading: const Icon(Icons.event_note_outlined),
                    title: Text(s.name),
                    subtitle: Text('${s.durationMinutes} min, ${s.price.toStringAsFixed(2)} KM'),
                    onTap: () => _openBookingFor(service: s),
                  ),
                )),
            const Divider(height: 32),
          ],
          Text('Preporučeno za vas', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          if (_error != null)
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))
          else if (_recommendations == null)
            const Center(child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()))
          else if (_recommendations!.isEmpty)
            const Text('Trenutno nema preporuka - zakažite prvi termin da bismo mogli personalizovati prijedloge.')
          else
            ..._recommendations!.map((r) => Card(
                  margin: const EdgeInsets.only(bottom: 12),
                  child: ListTile(
                    contentPadding: const EdgeInsets.all(12),
                    title: Text('${r.doctorName} — ${r.medicalServiceName}'),
                    subtitle: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('${r.locationName} · ${_dateFormat.format(r.suggestedStartUtc.toLocal())}'),
                        const SizedBox(height: 4),
                        Text(r.reason, style: const TextStyle(fontStyle: FontStyle.italic)),
                      ],
                    ),
                    isThreeLine: true,
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => Navigator.of(context).push<bool>(MaterialPageRoute(
                      builder: (_) => BookAppointmentScreen(
                        initialDoctorId: r.doctorId,
                        initialMedicalServiceId: r.medicalServiceId,
                        initialDate: r.suggestedStartUtc.toLocal(),
                      ),
                    )).then((booked) { if (booked == true) _load(); }),
                  ),
                )),
        ],
      ),
    );
  }
}
