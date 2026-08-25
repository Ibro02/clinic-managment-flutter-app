import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';

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
  const BookAppointmentScreen({super.key});

  @override
  State<BookAppointmentScreen> createState() => _BookAppointmentScreenState();
}

class _BookAppointmentScreenState extends State<BookAppointmentScreen> {
  late final AppointmentProvider _appointmentProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;

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

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _appointmentProvider = AppointmentProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _loadOptions();
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
      await _appointmentProvider.insert({
        'doctorId': _doctor!.id,
        'medicalServiceId': _service!.id,
        'startUtc': _selectedSlot!.toUtc().toIso8601String(),
      });
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
