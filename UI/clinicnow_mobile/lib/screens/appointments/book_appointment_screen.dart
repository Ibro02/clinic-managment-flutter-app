import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/recommendation.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/payment_provider.dart';
import '../../providers/recommendation_provider.dart';
import '../payments/payment_webview_screen.dart';

/// Multi-step patient booking flow (PLAN.md Phase 4 item 6): doctor → service
/// → date → time slot, every option a real DB-backed dropdown (no free text -
/// rulebook Part II §K) and every offered time slot genuinely free on the
/// server (rulebook §7 - never a client-side guess). `patientId` is
/// deliberately never sent - the server resolves the caller's own Patient
/// record from the JWT (rulebook §5).
///
/// There is no separate "clinic" step: a doctor practices at exactly one
/// clinic (`Doctor.locationId`, 1:1), so picking a doctor and a clinic
/// independently could describe a combination that doesn't exist in reality.
/// The clinic is shown - never chosen - right under the doctor field.
class BookAppointmentScreen extends StatefulWidget {
  final int? initialDoctorId;
  final int? initialMedicalServiceId;
  final DateTime? initialDate;

  const BookAppointmentScreen({super.key, this.initialDoctorId, this.initialMedicalServiceId, this.initialDate});

  @override
  State<BookAppointmentScreen> createState() => _BookAppointmentScreenState();
}

class _BookAppointmentScreenState extends State<BookAppointmentScreen> {
  late final AppointmentProvider _appointmentProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;
  late final RecommendationProvider _recommendationProvider;
  late final PaymentProvider _paymentProvider;

  static final _dateFormat = DateFormat('dd.MM.yyyy');
  static final _timeFormat = DateFormat('HH:mm');

  bool _isLoadingOptions = true;
  List<Doctor> _doctors = [];
  List<MedicalService> _services = [];

  Doctor? _doctor;
  MedicalService? _service;
  DateTime? _date;

  bool _isLoadingSlots = false;
  List<DateTime> _slots = [];
  DateTime? _selectedSlot;

  bool _isSubmitting = false;
  String? _error;

  AppointmentRecommendation? _topRecommendation;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _appointmentProvider = AppointmentProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _recommendationProvider = RecommendationProvider(authSession);
    _paymentProvider = PaymentProvider(authSession);
    _loadOptions();
    // The "Preporučeno" banner exists to surface a recommendation to someone
    // who arrived here without one. When the screen was pre-filled from a
    // recommendation card (or a search result), the user already has that
    // data, so re-running the server-side candidate scoring just to render
    // the banner is wasted work.
    if (widget.initialDoctorId == null) {
      _loadTopRecommendation();
    }
  }

  Future<void> _loadOptions() async {
    final doctors = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
    final services = await _serviceProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
    if (!mounted) return;
    setState(() {
      _doctors = doctors.resultList;
      _services = services.resultList;
      _isLoadingOptions = false;
    });

    if (widget.initialDoctorId != null || widget.initialMedicalServiceId != null) {
      setState(() {
        if (widget.initialDoctorId != null) {
          _doctor = _doctors.cast<Doctor?>().firstWhere((d) => d?.id == widget.initialDoctorId, orElse: () => null);
        }
        if (widget.initialMedicalServiceId != null) {
          _service = _services.cast<MedicalService?>().firstWhere((s) => s?.id == widget.initialMedicalServiceId, orElse: () => null);
        }
        if (widget.initialDate != null) {
          _date = widget.initialDate;
        }
      });
      if (_doctor != null && _service != null && _date != null) {
        await _loadSlots();
      }
    }
  }

  Future<void> _loadTopRecommendation() async {
    try {
      final recommendations = await _recommendationProvider.getRecommendations();
      if (mounted && recommendations.isNotEmpty) {
        setState(() => _topRecommendation = recommendations.first);
      }
    } catch (_) {
      // Best-effort - a failed recommendation fetch must never block booking.
    }
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: DateTime.now().add(const Duration(days: 1)),
      firstDate: DateTime.now(),
      lastDate: DateTime.now().add(const Duration(days: 180)),
    );
    if (picked == null) return;
    setState(() {
      _date = picked;
      _selectedSlot = null;
      _slots = [];
    });
    await _loadSlots();
  }

  Future<void> _loadSlots() async {
    if (_doctor == null || _service == null || _date == null) return;

    setState(() {
      _isLoadingSlots = true;
      _error = null;
    });
    try {
      final slots = await _appointmentProvider.availableSlots(
        doctorId: _doctor!.id,
        medicalServiceId: _service!.id,
        date: _date!,
      );
      if (!mounted) return;
      setState(() => _slots = slots);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoadingSlots = false);
    }
  }

  Future<void> _submit() async {
    if (_doctor == null || _service == null || _selectedSlot == null) {
      setState(() => _error = 'Odaberite doktora, uslugu i termin.');
      return;
    }

    setState(() {
      _isSubmitting = true;
      _error = null;
    });

    try {
      final appointment = await _appointmentProvider.insert({
        'doctorId': _doctor!.id,
        'medicalServiceId': _service!.id,
        'startUtc': _selectedSlot!.toUtc().toIso8601String(),
      });
      if (!mounted) return;

      final wantsToPay = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('Plaćanje'),
          content: Text('Termin je zakazan. Željeli biste li odmah platiti (${_service!.price.toStringAsFixed(2)} KM)?'),
          actions: [
            TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Kasnije')),
            FilledButton(onPressed: () => Navigator.of(dialogContext).pop(true), child: const Text('Plati sada')),
          ],
        ),
      );

      if (wantsToPay == true) {
        await _attemptPayment(appointment.id);
      }

      if (!mounted) return;
      Navigator.of(context).pop(true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Termin je uspješno zakazan.')),
      );
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _isSubmitting = false;
      });
    }
  }

  Future<void> _attemptPayment(int appointmentId) async {
    try {
      final payment = await _paymentProvider.create(appointmentId);
      if (!mounted || payment.approveUrl == null) return;

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      if (approved == true) {
        await _paymentProvider.capture(payment.id);
      }
      // A `false`/null result (cancelled) or a failed capture is silently
      // fine here - the appointment stays booked and unpaid either way
      // (design doc §2), and a "Plati" button remains available on the
      // appointment detail screen for a retry.
    } on ApiException {
      // Payment failures must never block the booking flow that already
      // succeeded - the appointment exists regardless.
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Zakazivanje termina'),
        actions: [
          IconButton(
            tooltip: 'Zatvori',
            icon: const Icon(Icons.close),
            onPressed: () => Navigator.of(context).pop(false),
          ),
        ],
      ),
      body: _isLoadingOptions
          ? const Center(child: CircularProgressIndicator())
          : SingleChildScrollView(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (_topRecommendation != null) ...[
                    Card(
                      color: Theme.of(context).colorScheme.secondaryContainer,
                      child: ListTile(
                        leading: const Icon(Icons.recommend_outlined),
                        title: Text('Preporučeno: ${_topRecommendation!.doctorName} — ${_topRecommendation!.medicalServiceName}'),
                        subtitle: Text(_topRecommendation!.reason),
                        trailing: TextButton(
                          child: const Text('Odaberi'),
                          onPressed: () {
                            final recommendation = _topRecommendation!;
                            setState(() {
                              _doctor = _doctors.cast<Doctor?>().firstWhere((d) => d?.id == recommendation.doctorId, orElse: () => null);
                              _service = _services.cast<MedicalService?>().firstWhere((s) => s?.id == recommendation.medicalServiceId, orElse: () => null);
                              _date = recommendation.suggestedStartUtc.toLocal();
                            });
                            _loadSlots();
                          },
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  Text('1. Odaberite doktora', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 8),
                  DropdownButtonFormField<Doctor>(
                    initialValue: _doctor,
                    decoration: const InputDecoration(border: OutlineInputBorder()),
                    // null, not the default 48 - each item is two lines
                    // (name/specializations + clinic), so a fixed single-line
                    // height would clip the clinic subtext.
                    itemHeight: null,
                    items: _doctors.map((d) => DropdownMenuItem(value: d, child: _DoctorOption(doctor: d))).toList(),
                    onChanged: (value) {
                      setState(() {
                        _doctor = value;
                        _date = null;
                        _selectedSlot = null;
                        _slots = [];
                      });
                      if (value != null) {
                        _recommendationProvider.logInteraction(type: InteractionType.doctorView, doctorId: value.id).catchError((_) {});
                      }
                    },
                  ),
                  const SizedBox(height: 20),
                  Text('2. Odaberite uslugu', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 8),
                  DropdownButtonFormField<MedicalService>(
                    initialValue: _service,
                    decoration: const InputDecoration(border: OutlineInputBorder()),
                    items: _services
                        .map((s) => DropdownMenuItem(value: s, child: Text('${s.name} (${s.durationMinutes} min, ${s.price.toStringAsFixed(2)} KM)')))
                        .toList(),
                    onChanged: (value) {
                      setState(() {
                        _service = value;
                        _selectedSlot = null;
                        _slots = [];
                      });
                      _loadSlots();
                      if (value != null) {
                        _recommendationProvider.logInteraction(type: InteractionType.medicalServiceView, medicalServiceId: value.id).catchError((_) {});
                      }
                    },
                  ),
                  const SizedBox(height: 20),
                  Text('3. Odaberite datum', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 8),
                  OutlinedButton.icon(
                    onPressed: (_doctor == null || _service == null) ? null : _pickDate,
                    icon: const Icon(Icons.calendar_today_outlined),
                    label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                  ),
                  const SizedBox(height: 20),
                  Text('4. Odaberite termin', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 8),
                  if (_isLoadingSlots)
                    const Center(child: CircularProgressIndicator())
                  else if (_date == null)
                    const Text('Prvo odaberite datum.')
                  else if (_slots.isEmpty)
                    const Text('Nema slobodnih termina za odabrani datum.')
                  else
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: _slots
                          .map((slot) => ChoiceChip(
                                label: Text(_timeFormat.format(slot)),
                                selected: _selectedSlot == slot,
                                onSelected: (_) => setState(() => _selectedSlot = slot),
                              ))
                          .toList(),
                    ),
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _isSubmitting ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Text('Zakaži termin'),
                  ),
                ],
              ),
            ),
    );
  }
}

/// A doctor dropdown item: name (+ specializations, if any), with the clinic
/// they practice at shown smaller underneath, colored per-clinic (rulebook
/// Part II §K) - reinforces which clinic a doctor belongs to at a glance
/// without adding an extra step to the booking flow.
class _DoctorOption extends StatelessWidget {
  final Doctor doctor;

  const _DoctorOption({required this.doctor});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text('${doctor.fullName}${doctor.specializations.isNotEmpty ? ' (${doctor.specializations.join(', ')})' : ''}'),
        Text(
          doctor.locationName,
          style: TextStyle(fontSize: 12, color: clinicColor(doctor.locationId)),
        ),
      ],
    );
  }
}
