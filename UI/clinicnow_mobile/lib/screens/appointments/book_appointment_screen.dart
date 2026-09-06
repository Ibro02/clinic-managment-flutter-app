import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/recommendation.dart';
import '../../models/referral.dart';
import '../../core/design_tokens.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/payment_provider.dart';
import '../../providers/recommendation_provider.dart';
import '../../providers/referral_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/doctor_dropdown_field.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';
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
/// How the optional pay-now step ended. The booking itself is already committed
/// server-side by the time any of these is produced - this only describes the
/// payment, which gets its own message rather than hiding behind the generic
/// booking-success snackbar.
enum _PaymentOutcome {
  /// The patient chose "Kasnije" - no payment was started.
  notAttempted,

  /// The patient opened PayPal and backed out without approving.
  cancelled,

  /// Approved at PayPal and captured successfully.
  paid,

  /// Something went wrong creating the order, or after approval while capturing.
  failed,
}

class BookAppointmentScreen extends StatefulWidget {
  final int? initialDoctorId;
  final int? initialMedicalServiceId;
  final DateTime? initialDate;

  /// Narrows the doctor dropdown to doctors qualified in this specialization
  /// (review item C5: "continue to booking with the appropriate specialist"
  /// from a referral). Filtered client-side from each `Doctor.specializationIds`
  /// already carried on the doctor list - no separate backend call needed.
  final int? initialSpecializationId;

  /// Set alongside [initialSpecializationId] when booking "from" a referral -
  /// sent to the server so it can link the new appointment back to the
  /// referral (`Referral.ResultingAppointmentId`) and archive the referral
  /// once that appointment is completed or cancelled. The server
  /// independently re-validates this referral is still usable (not archived,
  /// not already used, belongs to this patient) - the client never assumes
  /// it succeeded just because the button was shown.
  final int? referralId;

  const BookAppointmentScreen({
    super.key,
    this.initialDoctorId,
    this.initialMedicalServiceId,
    this.initialDate,
    this.initialSpecializationId,
    this.referralId,
  });

  @override
  State<BookAppointmentScreen> createState() => _BookAppointmentScreenState();
}

class _BookAppointmentScreenState extends State<BookAppointmentScreen> {
  late final AppointmentProvider _appointmentProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;
  late final RecommendationProvider _recommendationProvider;
  late final PaymentProvider _paymentProvider;
  late final ReferralProvider _referralProvider;

  static final _dateFormat = DateFormat('dd.MM.yyyy');
  static final _timeFormat = DateFormat('HH:mm');

  bool _isLoadingOptions = true;
  List<Doctor> _doctors = [];
  List<MedicalService> _services = [];

  /// The patient's own active (unused, non-archived) referrals - fetched once
  /// so the general booking flow can tell them, per service, whether they
  /// already have what a referral-required service needs.
  List<Referral> _ownReferrals = [];

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
    _referralProvider = ReferralProvider(authSession);
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
    final services = await _fetchServices(widget.initialDoctorId);
    final ownReferrals = await _loadOwnReferrals();
    if (!mounted) return;
    setState(() {
      _doctors = widget.initialSpecializationId == null
          ? doctors.resultList
          : doctors.resultList.where((d) => d.specializationIds.contains(widget.initialSpecializationId)).toList();
      _services = services;
      _ownReferrals = ownReferrals;
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
    } else if (widget.referralId != null) {
      await _prefillFromReferral();
    }
  }

  /// Best-effort - a failed fetch must never block booking, it just means the
  /// "you already have a referral for this" notice can't be shown.
  Future<List<Referral>> _loadOwnReferrals() async {
    try {
      return await _referralProvider.getPaged();
    } catch (_) {
      return [];
    }
  }

  /// Auto-fill when arriving from a referral (review item C5 follow-up,
  /// requested directly by Ibrahim): picks the first specialist and service
  /// under the referral's specialization, then proposes the earliest open
  /// slot within the next two weeks. Every field this touches
  /// (doctor/service/date/slot) stays exactly as editable as it always was -
  /// this only saves the patient the first few taps, it never locks anything.
  Future<void> _prefillFromReferral() async {
    if (_doctors.isEmpty) return;
    final doctor = _doctors.first;

    final services = await _fetchServices(doctor.id);
    if (!mounted || services.isEmpty) return;
    final service = services.first;

    setState(() {
      _doctor = doctor;
      _services = services;
      _service = service;
    });

    const searchWindowDays = 14;
    for (var offset = 1; offset <= searchWindowDays; offset++) {
      final date = DateTime.now().add(Duration(days: offset));
      try {
        final slots = await _appointmentProvider.availableSlots(
          doctorId: doctor.id,
          medicalServiceId: service.id,
          date: date,
        );
        if (!mounted) return;
        if (slots.isNotEmpty) {
          setState(() {
            _date = date;
            _slots = slots;
            _selectedSlot = slots.first;
          });
          return;
        }
      } on ApiException {
        return;
      }
    }
  }

  /// Services the given doctor is actually qualified for. The filtering is done
  /// by the API (`doctorId` on the search object), not in the widget, so the
  /// dropdown can only ever offer pairings the booking endpoint would accept -
  /// the server enforces the same rule independently (review item C2).
  Future<List<MedicalService>> _fetchServices(int? doctorId) async {
    final result = await _serviceProvider.getPaged({
      'pageSize': 100,
      'orderBy': 'Name',
      'doctorId': ?doctorId,
    });
    return result.resultList;
  }

  /// Reloads the service list for the chosen doctor. The caller is
  /// responsible for having already cleared `_service` (and everything after
  /// it) synchronously - a step-gated flow means switching doctors always
  /// starts the rest of the form over, never silently keeps a still-valid
  /// pick from the old doctor.
  Future<void> _reloadServicesForDoctor(int? doctorId) async {
    final services = await _fetchServices(doctorId);
    if (!mounted) return;
    setState(() => _services = services);
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

  /// An active, unused referral of the patient's own that covers the
  /// selected service's specialization - found automatically so the general
  /// booking flow (review item, new feature request) can tell them they're
  /// already covered without making them go find it themselves. Null when a
  /// referral was already supplied explicitly (the Uputnice -> "Zakaži
  /// termin" flow), since that one is used as-is.
  Referral? get _matchingReferral {
    if (widget.referralId != null) return null;
    final service = _service;
    if (service == null || !service.isReferralRequired) return null;
    for (final referral in _ownReferrals) {
      if (!referral.isUsed && referral.targetSpecializationId == service.specializationId) return referral;
    }
    return null;
  }

  /// True when the selected service needs a referral this booking doesn't
  /// carry and none of the patient's own active referrals cover it either.
  bool get _isMissingRequiredReferral =>
      _service?.isReferralRequired == true && widget.referralId == null && _matchingReferral == null;

  Future<void> _submit() async {
    if (_doctor == null || _service == null || _selectedSlot == null) {
      setState(() => _error = 'Odaberite doktora, uslugu i termin.');
      return;
    }
    if (_isMissingRequiredReferral) {
      setState(() => _error = 'Ova usluga zahtijeva uputnicu. Zakažite je iz sekcije "Uputnice".');
      return;
    }

    final effectiveReferralId = widget.referralId ?? _matchingReferral?.id;

    setState(() {
      _isSubmitting = true;
      _error = null;
    });

    try {
      final appointment = await _appointmentProvider.insert({
        'doctorId': _doctor!.id,
        'medicalServiceId': _service!.id,
        'startUtc': _selectedSlot!.toUtc().toIso8601String(),
        'referralId': ?effectiveReferralId,
      });
      if (!mounted) return;

      final wantsToPay = await showAppDialog<bool>(
        context: context,
        builder: (dialogContext) => AppDialog(
          title: 'Plaćanje',
          subtitle: 'Termin je uspješno zakazan.',
          icon: Icons.payment_outlined,
          tone: AppTone.success,
          showClose: false,
          actions: [
            OutlinedButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Kasnije'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: const Text('Plati sada'),
            ),
          ],
          child: Text(
            'Željeli biste li odmah platiti '
            '${_service!.price.toStringAsFixed(2)} KM? '
            'Plaćanje možete izvršiti i kasnije sa ekrana termina.',
            style: dialogContext.text.bodyMedium?.copyWith(
              color: dialogContext.colors.textSecondary,
              height: 1.5,
            ),
          ),
        ),
      );

      var outcome = _PaymentOutcome.notAttempted;
      if (wantsToPay == true) {
        outcome = await _attemptPayment(appointment.id);
      }

      if (!mounted) return;
      Navigator.of(context).pop(true);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(switch (outcome) {
          // The booking always succeeded by this point, so it is always
          // reported as such - but a patient who just authorized a real payment
          // at PayPal is told, separately and honestly, whether it went through.
          _PaymentOutcome.paid => 'Termin zakazan i plaćanje uspješno izvršeno.',
          _PaymentOutcome.failed => 'Termin je zakazan, ali plaćanje nije uspjelo. Možete platiti kasnije sa ekrana termina.',
          _ => 'Termin je uspješno zakazan.',
        })),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      // The payment step can fail in ways neither catch clause here handles
      // (a dropped connection surfaces as ClientException, not ApiException).
      // Without this the button would stay stuck in its spinner forever on a
      // booking that actually succeeded.
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  Future<_PaymentOutcome> _attemptPayment(int appointmentId) async {
    try {
      final payment = await _paymentProvider.create(appointmentId);
      if (!mounted || payment.approveUrl == null) return _PaymentOutcome.failed;

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      // Cancelling at PayPal isn't a failure - the patient chose not to pay, the
      // appointment stays booked and unpaid (design doc §2), and a "Plati"
      // button remains on the appointment detail screen for a retry.
      if (approved != true) return _PaymentOutcome.cancelled;

      await _paymentProvider.capture(payment.id);
      return _PaymentOutcome.paid;
    } catch (_) {
      // Deliberately broad: this is a best-effort step after a booking that
      // already succeeded, and it must never block or crash that flow - not for
      // an ApiException, and not for a ClientException/TimeoutException/
      // FormatException from a dropped mobile connection mid-payment either.
      // The outcome is reported to the user by the caller rather than swallowed.
      return _PaymentOutcome.failed;
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
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // The recommender's top pick, offered as a one-tap shortcut
                  // that fills the whole form. Its reason is shown, never just
                  // the suggestion (rulebook §I: the recommender is explainable).
                  if (_topRecommendation != null) ...[
                    _recommendationCard(context),
                    const SizedBox(height: AppSpacing.md),
                  ],
                  if (widget.initialSpecializationId != null) ...[
                    const AppNotice(
                      tone: AppTone.info,
                      message: 'Lista je sužena na doktore prema vašoj uputnici.',
                    ),
                    const SizedBox(height: AppSpacing.md),
                  ],
                  _step(context, 1, 'Odaberite doktora'),
                  DoctorDropdownField(
                    doctors: _doctors,
                    value: _doctor,
                    onChanged: (value) {
                      setState(() {
                        _doctor = value;
                        // Step-gated flow: switching doctors always starts the
                        // rest of the form over, never silently keeps a pick
                        // that happens to still be valid for the new doctor.
                        _service = null;
                        _services = [];
                        _date = null;
                        _selectedSlot = null;
                        _slots = [];
                      });
                      // Narrow the service list to what this doctor can perform.
                      _reloadServicesForDoctor(value?.id);
                      if (value != null) {
                        _recommendationProvider
                            .logInteraction(type: InteractionType.doctorView, doctorId: value.id)
                            .catchError((_) {});
                      }
                    },
                  ),
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 2, 'Odaberite uslugu'),
                  DropdownButtonFormField<MedicalService>(
                    initialValue: _service,
                    decoration: InputDecoration(
                      hintText: 'Odaberite uslugu',
                      helperText: _doctor == null ? 'Prvo odaberite doktora.' : null,
                    ),
                    items: _services
                        .map(
                          (s) => DropdownMenuItem(
                            value: s,
                            child: Text(
                              '${s.name} (${s.durationMinutes} min, ${s.price.toStringAsFixed(2)} KM)',
                            ),
                          ),
                        )
                        .toList(),
                    onChanged: _doctor == null
                        ? null
                        : (value) {
                            setState(() {
                              _service = value;
                              _selectedSlot = null;
                              _slots = [];
                            });
                            _loadSlots();
                            if (value != null) {
                              _recommendationProvider
                                  .logInteraction(
                                    type: InteractionType.medicalServiceView,
                                    medicalServiceId: value.id,
                                  )
                                  .catchError((_) {});
                            }
                          },
                  ),
                  if (_isMissingRequiredReferral) ...[
                    const SizedBox(height: AppSpacing.xs),
                    const AppNotice(
                      tone: AppTone.warning,
                      message: 'Ova usluga zahtijeva aktivnu uputnicu. Zakažite je iz sekcije "Uputnice".',
                    ),
                  ] else if (_matchingReferral != null) ...[
                    const SizedBox(height: AppSpacing.xs),
                    AppNotice(
                      tone: AppTone.success,
                      message:
                          'Imate aktivnu uputnicu za ovu uslugu (${_matchingReferral!.targetSpecializationName}) - '
                          'biće automatski iskorištena za ovaj termin.',
                    ),
                  ],
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 3, 'Odaberite datum'),
                  SizedBox(
                    height: 48,
                    child: OutlinedButton.icon(
                      onPressed: (_doctor == null || _service == null) ? null : _pickDate,
                      icon: const Icon(Icons.calendar_today_outlined, size: 18),
                      label: Text(_date == null ? 'Odaberite datum' : _dateFormat.format(_date!)),
                      style: OutlinedButton.styleFrom(alignment: Alignment.centerLeft),
                    ),
                  ),
                  if (_doctor == null || _service == null)
                    Padding(
                      padding: const EdgeInsets.only(top: 5),
                      child: Text(
                        'Prvo odaberite doktora i uslugu.',
                        style: context.text.bodySmall?.copyWith(color: context.colors.textMuted, fontSize: 12),
                      ),
                    ),
                  const SizedBox(height: AppSpacing.lg),
                  _step(context, 4, 'Odaberite termin'),
                  _slotSection(context),
                  if (_error != null) ...[
                    const SizedBox(height: AppSpacing.md),
                    AppNotice(tone: AppTone.danger, message: _error!),
                  ],
                  const SizedBox(height: AppSpacing.lg),
                  FilledButton(
                    onPressed: (_isSubmitting || _isMissingRequiredReferral) ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Text('Zakaži termin'),
                  ),
                ],
              ),
            ),
    );
  }

  /// A numbered step label. The booking flow is four decisions in order, and
  /// numbering them makes that sequence visible instead of leaving four
  /// similar-looking fields stacked up.
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

  Widget _recommendationCard(BuildContext context) {
    final recommendation = _topRecommendation!;

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.sm + 2),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.recommend_outlined, size: 18, color: context.colors.primary),
              const SizedBox(width: AppSpacing.xs),
              Text('Preporučeno za vas', style: context.text.labelSmall),
            ],
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            '${recommendation.doctorName} — ${recommendation.medicalServiceName}',
            style: context.text.titleSmall,
          ),
          const SizedBox(height: 3),
          Text(
            recommendation.reason,
            style: context.text.bodySmall?.copyWith(color: context.colors.textSecondary),
          ),
          const SizedBox(height: AppSpacing.sm),
          Align(
            alignment: Alignment.centerRight,
            child: FilledButton(
              onPressed: () {
                setState(() {
                  _doctor = _doctors
                      .cast<Doctor?>()
                      .firstWhere((d) => d?.id == recommendation.doctorId, orElse: () => null);
                  _service = _services
                      .cast<MedicalService?>()
                      .firstWhere((s) => s?.id == recommendation.medicalServiceId, orElse: () => null);
                  _date = recommendation.suggestedStartUtc.toLocal();
                });
                _loadSlots();
              },
              child: const Text('Odaberi'),
            ),
          ),
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
