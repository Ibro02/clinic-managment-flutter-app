import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../core/design_tokens.dart';
import '../../models/appointment.dart';
import '../../models/doctor.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

/// Moves an existing appointment to a new doctor and/or time (review item
/// C6). Deliberately narrower than [ScheduleAppointmentDialog]: the patient
/// and the medical service can't change here (a reschedule is "another date,
/// time, or doctor", not a different patient or service), so there is no
/// patient/service picker - only the doctor, date, and slot the reschedule
/// endpoint actually accepts. Every check re-run there (working hours,
/// blocks, overlap, doctor↔service compatibility) is the same one a fresh
/// booking runs, so this dialog only has to offer good choices, not validate
/// them itself.
class RescheduleAppointmentDialog extends StatefulWidget {
  final Appointment appointment;

  const RescheduleAppointmentDialog({super.key, required this.appointment});

  @override
  State<RescheduleAppointmentDialog> createState() => _RescheduleAppointmentDialogState();
}

class _RescheduleAppointmentDialogState extends State<RescheduleAppointmentDialog> {
  late final AppointmentProvider _appointmentProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;

  static final _dateFormat = DateFormat('dd.MM.yyyy');
  static final _timeFormat = DateFormat('HH:mm');

  bool _isLoadingDoctors = true;
  List<Doctor> _doctors = [];
  Doctor? _doctor;
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
    _loadDoctors();
  }

  /// Only doctors qualified for this appointment's (unchangeable) medical
  /// service - the same compatibility rule the reschedule endpoint enforces
  /// (review item C2), so the dropdown can't offer a doctor the server would
  /// then reject. The service's specialization isn't on the appointment
  /// itself, so it's looked up once here.
  Future<void> _loadDoctors() async {
    final service = await _serviceProvider.getById(widget.appointment.medicalServiceId);
    final doctors = await _doctorProvider.getPaged({
      'pageSize': 100,
      'orderBy': 'LastName',
      'specializationId': service.specializationId,
    });
    if (!mounted) return;
    setState(() {
      _doctors = doctors.resultList;
      _doctor = _doctors.cast<Doctor?>().firstWhere((d) => d?.id == widget.appointment.doctorId, orElse: () => null);
      _isLoadingDoctors = false;
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
    if (_doctor == null || _date == null) return;

    setState(() => _isLoadingSlots = true);
    try {
      final slots = await _appointmentProvider.availableSlots(
        doctorId: _doctor!.id,
        medicalServiceId: widget.appointment.medicalServiceId,
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
    if (_doctor == null || _selectedSlot == null) {
      setState(() => _error = 'Odaberite doktora i termin.');
      return;
    }

    setState(() {
      _isSubmitting = true;
      _error = null;
    });

    try {
      await _appointmentProvider.reschedule(widget.appointment.id, doctorId: _doctor!.id, startUtc: _selectedSlot!);
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
    return AppDialog(
      title: 'Premještanje termina',
      subtitle: '${widget.appointment.patientName} — ${widget.appointment.medicalServiceName} '
          '(usluga se ne mijenja premještanjem).',
      icon: Icons.edit_calendar_outlined,
      width: 560,
      actions: [
        OutlinedButton(
          onPressed: _isSubmitting ? null : () => Navigator.of(context).pop(false),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: _isSubmitting ? null : _submit,
          child: _isSubmitting
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Premjesti'),
        ),
      ],
      child: _isLoadingDoctors
          ? const SizedBox(height: 140, child: Center(child: CircularProgressIndicator()))
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                AppFormSection(
                  children: [
                    AppField(
                      label: 'Doktor',
                      required: true,
                      help: _doctor == null ? null : 'Klinika: ${_doctor!.locationName}',
                      child: DropdownButtonFormField<Doctor>(
                        initialValue: _doctor,
                        decoration: const InputDecoration(hintText: 'Odaberite doktora'),
                        // null, not the default 48 - each item is two lines
                        // (name + clinic), so a fixed single-line height would
                        // clip the clinic subtext.
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
                    ),
                    AppField(
                      label: 'Datum',
                      required: true,
                      help: _doctor == null ? 'Prvo odaberite doktora.' : null,
                      child: SizedBox(
                        height: AppSizes.controlHeight,
                        child: OutlinedButton.icon(
                          onPressed: _doctor == null ? null : _pickDate,
                          icon: const Icon(Icons.calendar_today_outlined, size: 16),
                          label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                          style: OutlinedButton.styleFrom(alignment: Alignment.centerLeft),
                        ),
                      ),
                    ),
                  ],
                ),
                if (_date != null && _doctor != null) ...[
                  const SizedBox(height: AppSpacing.lg),
                  AppFormSection(label: 'Slobodni termini', children: [_slotPicker(context)]),
                ],
                if (_error != null) ...[
                  const SizedBox(height: AppSpacing.md),
                  AppNotice(tone: AppTone.danger, message: _error!),
                ],
              ],
            ),
    );
  }

  Widget _slotPicker(BuildContext context) {
    if (_isLoadingSlots) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: AppSpacing.md),
        child: Center(child: CircularProgressIndicator()),
      );
    }

    if (_slots.isEmpty) {
      return AppNotice(
        tone: AppTone.warning,
        message: 'Nema slobodnih termina za odabrani datum. Pokušajte s drugim datumom.',
      );
    }

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: _slots
          .map(
            (slot) => ChoiceChip(
              label: Text(_timeFormat.format(slot)),
              selected: _selectedSlot == slot,
              onSelected: (_) => setState(() => _selectedSlot = slot),
            ),
          )
          .toList(),
    );
  }
}

/// A doctor dropdown item: name, with the clinic they practice at shown
/// smaller underneath, colored per-clinic - matches
/// `ScheduleAppointmentDialog`'s own `_DoctorOption` (kept as a private copy
/// there and here rather than shared, since desktop's version - unlike
/// mobile's - never grew a specialization list and so never needed the
/// isExpanded/selectedItemBuilder fix review item C2 required on mobile).
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
