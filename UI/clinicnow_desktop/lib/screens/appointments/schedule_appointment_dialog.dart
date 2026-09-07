import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../core/design_tokens.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/patient.dart';
import '../../models/referral.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/patient_provider.dart';
import '../../providers/referral_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

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
  late final ReferralProvider _referralProvider;

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

  bool _isLoadingReferrals = false;
  List<Referral> _matchingReferrals = [];
  int? _referralId;

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
    _referralProvider = ReferralProvider(authSession);
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

  /// Narrows the service list to what the chosen doctor is qualified to perform.
  /// The filter is applied by the API (`doctorId` on the search object), so staff
  /// are only ever offered pairings the booking endpoint would accept - the
  /// server enforces the same rule independently (review item C2).
  Future<void> _reloadServicesForDoctor(int? doctorId) async {
    final result = await _serviceProvider.getPaged({
      'pageSize': 100,
      'orderBy': 'Name',
      'doctorId': ?doctorId,
    });
    if (!mounted) return;
    setState(() {
      _services = result.resultList;
      // Drop a selection the new doctor can't perform, so the form can't submit
      // a pairing the server will reject.
      if (!_services.any((s) => s.id == _medicalServiceId)) {
        _medicalServiceId = null;
        _selectedSlot = null;
        _slots = [];
      }
    });
    await _reloadReferralsIfNeeded();
  }

  MedicalService? get _selectedService =>
      _services.cast<MedicalService?>().firstWhere((s) => s?.id == _medicalServiceId, orElse: () => null);

  /// Fetches the chosen patient's active referrals matching the selected
  /// service's required specialization, whenever both a patient and a
  /// referral-required service are selected. Clears the previous selection
  /// otherwise, so a stale referralId can never survive a patient/service
  /// change onto a pairing it wasn't validated against.
  Future<void> _reloadReferralsIfNeeded() async {
    final service = _selectedService;
    if (_patientId == null || service == null || !service.isReferralRequired) {
      setState(() {
        _matchingReferrals = [];
        _referralId = null;
      });
      return;
    }

    setState(() => _isLoadingReferrals = true);
    final referrals = await _referralProvider.getPaged(patientId: _patientId);
    if (!mounted) return;
    setState(() {
      _matchingReferrals = referrals.where((r) => !r.isUsed && r.targetSpecializationId == service.specializationId).toList();
      if (!_matchingReferrals.any((r) => r.id == _referralId)) {
        _referralId = null;
      }
      _isLoadingReferrals = false;
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
    if (_selectedService?.isReferralRequired == true && _referralId == null) {
      setState(() => _error = 'Odaberite uputnicu za ovu uslugu.');
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
        if (_referralId != null) 'referralId': _referralId,
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
    return AppDialog(
      title: 'Novi termin',
      subtitle: 'Slobodni termini se dohvaćaju sa servera za odabranog doktora i uslugu.',
      icon: Icons.event_available_outlined,
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
              : const Text('Zakaži'),
        ),
      ],
      child: _isLoadingOptions
          ? const SizedBox(height: 140, child: Center(child: CircularProgressIndicator()))
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                AppFormSection(
                  children: [
                    AppField(
                      label: 'Pacijent',
                      required: true,
                      child: DropdownButtonFormField<int>(
                        initialValue: _patientId,
                        decoration: const InputDecoration(hintText: 'Odaberite pacijenta'),
                        items: _patients
                            .map((p) => DropdownMenuItem(value: p.id, child: Text(p.fullName)))
                            .toList(),
                        onChanged: (value) {
                          setState(() => _patientId = value);
                          _reloadReferralsIfNeeded();
                        },
                      ),
                    ),
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
                        isExpanded: true,
                        // The *closed* field draws the selected item too, and
                        // it only has AppSizes.controlHeight to draw it in - so
                        // the two-line _DoctorOption overflowed there. The
                        // popup keeps both lines; the field shows one, and the
                        // clinic is already on the `help` line above.
                        selectedItemBuilder: (context) => _doctors
                            .map(
                              (d) => Align(
                                alignment: Alignment.centerLeft,
                                child: Text(d.fullName, maxLines: 1, overflow: TextOverflow.ellipsis),
                              ),
                            )
                            .toList(),
                        items: _doctors
                            .map(
                              (d) => DropdownMenuItem(
                                value: d,
                                child: _DoctorOption(doctor: d),
                              ),
                            )
                            .toList(),
                        onChanged: (value) {
                          setState(() {
                            _doctor = value;
                            _selectedSlot = null;
                            _slots = [];
                          });
                          _reloadServicesForDoctor(value?.id);
                          _loadSlots();
                        },
                      ),
                    ),
                    AppField(
                      label: 'Usluga',
                      required: true,
                      child: DropdownButtonFormField<int>(
                        initialValue: _medicalServiceId,
                        decoration: const InputDecoration(hintText: 'Odaberite uslugu'),
                        items: _services
                            .map(
                              (s) => DropdownMenuItem(
                                value: s.id,
                                child: Text('${s.name} (${s.durationMinutes} min)'),
                              ),
                            )
                            .toList(),
                        onChanged: (value) {
                          setState(() {
                            _medicalServiceId = value;
                            _selectedSlot = null;
                            _slots = [];
                          });
                          _loadSlots();
                          _reloadReferralsIfNeeded();
                        },
                      ),
                    ),
                    if (_selectedService?.isReferralRequired == true)
                      AppField(
                        label: 'Uputnica',
                        required: true,
                        help: _patientId == null
                            ? 'Prvo odaberite pacijenta.'
                            : _isLoadingReferrals
                            ? null
                            : _matchingReferrals.isEmpty
                            ? 'Pacijent nema aktivnu uputnicu za ovu specijalizaciju.'
                            : null,
                        child: _isLoadingReferrals
                            ? const LinearProgressIndicator()
                            : DropdownButtonFormField<int>(
                                initialValue: _referralId,
                                decoration: const InputDecoration(hintText: 'Odaberite uputnicu'),
                                items: _matchingReferrals
                                    .map((r) => DropdownMenuItem(value: r.id, child: Text(r.reason)))
                                    .toList(),
                                onChanged: (value) => setState(() => _referralId = value),
                              ),
                      ),
                    // Rulebook §K: an action whose preconditions aren't met is
                    // disabled *and* says why, rather than silently doing nothing.
                    AppField(
                      label: 'Datum',
                      required: true,
                      help: (_doctor == null || _medicalServiceId == null)
                          ? 'Prvo odaberite doktora i uslugu.'
                          : (_selectedService?.isReferralRequired == true && _referralId == null)
                          ? 'Prvo odaberite uputnicu.'
                          : null,
                      child: SizedBox(
                        height: AppSizes.controlHeight,
                        child: OutlinedButton.icon(
                          onPressed:
                              (_doctor == null ||
                                  _medicalServiceId == null ||
                                  (_selectedService?.isReferralRequired == true && _referralId == null))
                              ? null
                              : _pickDate,
                          icon: const Icon(Icons.calendar_today_outlined, size: 16),
                          label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                          style: OutlinedButton.styleFrom(alignment: Alignment.centerLeft),
                        ),
                      ),
                    ),
                  ],
                ),
                if (_date != null && _doctor != null && _medicalServiceId != null) ...[
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
