import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/appointment.dart';
import '../../models/doctor.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../widgets/doctor_dropdown_field.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';

/// Moves an existing appointment to a new doctor and/or time (review item
/// C6). Deliberately narrower than [BookAppointmentScreen]: the medical
/// service can't change here (a reschedule is "another date, time, or
/// doctor", not a different service), so there is no service step - only the
/// doctor, date, and slot the reschedule endpoint actually accepts. Every
/// check re-run there (working hours, blocks, overlap, doctor↔service
/// compatibility) is the same one a fresh booking runs, so this screen only
/// has to offer good choices, not validate them itself.
class RescheduleAppointmentScreen extends StatefulWidget {
  final Appointment appointment;
  final AppointmentProvider provider;

  const RescheduleAppointmentScreen({super.key, required this.appointment, required this.provider});

  @override
  State<RescheduleAppointmentScreen> createState() => _RescheduleAppointmentScreenState();
}

class _RescheduleAppointmentScreenState extends State<RescheduleAppointmentScreen> {
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
      'orderBy': 'User.LastName',
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

    setState(() {
      _isLoadingSlots = true;
      _error = null;
    });
    try {
      final slots = await widget.provider.availableSlots(
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
      await widget.provider.reschedule(widget.appointment.id, doctorId: _doctor!.id, startUtc: _selectedSlot!);
      if (!mounted) return;
      Navigator.of(context).pop(true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Termin je premješten.')),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Premještanje termina'),
        actions: [
          IconButton(
            tooltip: 'Zatvori',
            icon: const Icon(Icons.close),
            onPressed: () => Navigator.of(context).pop(false),
          ),
        ],
      ),
      body: _isLoadingDoctors
          ? const Center(child: CircularProgressIndicator())
          : SingleChildScrollView(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  AppNotice(
                    tone: AppTone.neutral,
                    message: 'Usluga (${widget.appointment.medicalServiceName}) se ne mijenja premještanjem termina.',
                  ),
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 1, 'Odaberite doktora'),
                  DoctorDropdownField(
                    doctors: _doctors,
                    value: _doctor,
                    onChanged: (value) {
                      setState(() {
                        _doctor = value;
                        _date = null;
                        _selectedSlot = null;
                        _slots = [];
                      });
                    },
                  ),
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 2, 'Odaberite datum'),
                  SizedBox(
                    height: 48,
                    child: OutlinedButton.icon(
                      onPressed: _doctor == null ? null : _pickDate,
                      icon: const Icon(Icons.calendar_today_outlined, size: 18),
                      label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                      style: OutlinedButton.styleFrom(alignment: Alignment.centerLeft),
                    ),
                  ),
                  if (_doctor == null)
                    Padding(
                      padding: const EdgeInsets.only(top: 5),
                      child: Text(
                        'Prvo odaberite doktora.',
                        style: context.text.bodySmall?.copyWith(color: context.colors.textMuted, fontSize: 12),
                      ),
                    ),
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 3, 'Odaberite termin'),
                  _slotSection(context),
                  if (_error != null) ...[
                    const SizedBox(height: AppSpacing.md),
                    AppNotice(tone: AppTone.danger, message: _error!),
                  ],
                  const SizedBox(height: AppSpacing.lg),
                  FilledButton(
                    onPressed: _isSubmitting ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Text('Premjesti termin'),
                  ),
                ],
              ),
            ),
    );
  }

  Widget _step(BuildContext context, int number, String label) {
    final c = context.colors;

    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.xs),
      child: Row(
        children: [
          Container(
            width: 22,
            height: 22,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: c.primarySoft,
              borderRadius: AppRadius.all(AppRadius.xs),
            ),
            child: Text(
              '$number',
              style: context.text.labelMedium?.copyWith(color: c.primary, fontSize: 12),
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Text(label, style: context.text.titleSmall),
        ],
      ),
    );
  }

  Widget _slotSection(BuildContext context) {
    if (_isLoadingSlots) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: AppSpacing.md),
        child: Center(child: CircularProgressIndicator()),
      );
    }
    if (_date == null) {
      return AppNotice(tone: AppTone.neutral, message: 'Prvo odaberite datum.');
    }
    if (_slots.isEmpty) {
      return AppNotice(
        tone: AppTone.warning,
        message: 'Nema slobodnih termina za odabrani datum. Pokušajte s drugim datumom.',
      );
    }

    return AppSlotPicker(
      slots: _slots,
      selected: _selectedSlot,
      onSelected: (slot) => setState(() => _selectedSlot = slot),
      labelBuilder: _timeFormat.format,
    );
  }
}
