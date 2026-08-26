import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/patient.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/patient_provider.dart';

/// Staff-side booking dialog: patient + doctor + service dropdowns (all from
/// the DB, never free text - rulebook Part II §K), then a date picker, then
/// the *real* free slots for that doctor+service+day fetched from the backend
/// (never client-computed - rulebook §7).
///
/// There is deliberately no separate "clinic" dropdown: a doctor practices at
/// exactly one clinic (`Doctor.locationId`, 1:1), so picking a doctor and a
/// clinic independently could describe a combination that doesn't exist in
/// reality. The clinic is shown - never chosen - right under the doctor field
/// as soon as one is selected.
class ScheduleAppointmentDialog extends StatefulWidget {
  const ScheduleAppointmentDialog({super.key});

  @override
  State<ScheduleAppointmentDialog> createState() => _ScheduleAppointmentDialogState();
}

class _ScheduleAppointmentDialogState extends State<ScheduleAppointmentDialog> {
  late final AppointmentProvider _appointmentProvider;
  late final PatientProvider _patientProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;

  static final _dateFormat = DateFormat('dd.MM.yyyy');
  static final _timeFormat = DateFormat('HH:mm');

  bool _isLoadingOptions = true;
  List<Patient> _patients = [];
  List<Doctor> _doctors = [];
  List<MedicalService> _services = [];

  int? _patientId;
  Doctor? _doctor;
  int? _medicalServiceId;
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
    _patientProvider = PatientProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _loadOptions();
  }

  Future<void> _loadOptions() async {
    final results = await Future.wait([
      _patientProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'}),
      _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'}),
      _serviceProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'}),
    ]);
    if (!mounted) return;
    setState(() {
      _patients = (results[0].resultList as List<Patient>);
      _doctors = (results[1].resultList as List<Doctor>);
      _services = (results[2].resultList as List<MedicalService>);
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
    if (_doctor == null || _medicalServiceId == null || _date == null) return;

    setState(() => _isLoadingSlots = true);
    try {
      final slots = await _appointmentProvider.availableSlots(
        doctorId: _doctor!.id,
        medicalServiceId: _medicalServiceId!,
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
    if (_patientId == null || _doctor == null || _medicalServiceId == null || _selectedSlot == null) {
      setState(() => _error = 'Popunite sva polja i odaberite termin.');
      return;
    }

    setState(() {
      _isSubmitting = true;
      _error = null;
    });

    try {
      await _appointmentProvider.insert({
        'patientId': _patientId,
        'doctorId': _doctor!.id,
        'medicalServiceId': _medicalServiceId,
        'startUtc': _selectedSlot!.toUtc().toIso8601String(),
      });
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _isSubmitting = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Novi termin'),
      content: SizedBox(
        width: 480,
        child: _isLoadingOptions
            ? const SizedBox(height: 120, child: Center(child: CircularProgressIndicator()))
            : SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    DropdownButtonFormField<int>(
                      initialValue: _patientId,
                      decoration: const InputDecoration(labelText: 'Pacijent'),
                      items: _patients.map((p) => DropdownMenuItem(value: p.id, child: Text(p.fullName))).toList(),
                      onChanged: (value) => setState(() => _patientId = value),
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<Doctor>(
                      initialValue: _doctor,
                      decoration: const InputDecoration(labelText: 'Doktor'),
                      // null, not the default 48 - each item is two lines
                      // (name + clinic), so a fixed single-line height would
                      // clip the clinic subtext.
                      itemHeight: null,
                      items: _doctors.map((d) => DropdownMenuItem(value: d, child: _DoctorOption(doctor: d))).toList(),
                      onChanged: (value) {
                        setState(() {
                          _doctor = value;
                          _selectedSlot = null;
                          _slots = [];
                        });
                        _loadSlots();
                      },
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<int>(
                      initialValue: _medicalServiceId,
                      decoration: const InputDecoration(labelText: 'Usluga'),
                      items: _services
                          .map((s) => DropdownMenuItem(value: s.id, child: Text('${s.name} (${s.durationMinutes} min)')))
                          .toList(),
                      onChanged: (value) {
                        setState(() {
                          _medicalServiceId = value;
                          _selectedSlot = null;
                          _slots = [];
                        });
                        _loadSlots();
                      },
                    ),
                    const SizedBox(height: 12),
                    OutlinedButton.icon(
                      onPressed: (_doctor == null || _medicalServiceId == null) ? null : _pickDate,
                      icon: const Icon(Icons.calendar_today_outlined),
                      label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                    ),
                    const SizedBox(height: 12),
                    if (_isLoadingSlots)
                      const Center(child: CircularProgressIndicator())
                    else if (_date != null && _doctor != null && _medicalServiceId != null)
                      _slots.isEmpty
                          ? const Text('Nema slobodnih termina za odabrani datum.')
                          : Wrap(
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
                      const SizedBox(height: 12),
                      Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                    ],
                  ],
                ),
              ),
      ),
      actions: [
        TextButton(
          onPressed: _isSubmitting ? null : () => Navigator.of(context).pop(false),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: _isSubmitting ? null : _submit,
          child: _isSubmitting
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Zakaži'),
        ),
      ],
    );
  }
}

/// A doctor dropdown item: name, with the clinic they practice at shown
/// smaller underneath, colored per-clinic (rulebook Part II §K: dropdowns
/// from the DB, and this reinforces which of two same-named-looking doctors
/// belongs to which clinic without adding an extra column/step to read).
class _DoctorOption extends StatelessWidget {
  final Doctor doctor;

  const _DoctorOption({required this.doctor});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(doctor.fullName),
        Text(
          doctor.locationName,
          style: TextStyle(fontSize: 12, color: clinicColor(doctor.locationId, Theme.of(context).brightness)),
        ),
      ],
    );
  }
}
