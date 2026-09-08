import '../../core/base_provider.dart';
import '../../core/api_http.dart';
import 'dart:async';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/roles.dart';
import '../../models/appointment.dart';
import '../../models/lab_finding.dart';
import '../../models/referral.dart';
import '../../models/specialization.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/lab_finding_provider.dart';
import '../../providers/referral_provider.dart';
import '../../providers/specialization_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_fields.dart';
import '../../widgets/ui/app_states.dart';
import '../people/medical_record_screen.dart';

/// Entry and review of a patient's specialist referrals ("uputnice", review
/// item C5). Reached either from a row action on the patients grid (the
/// doctor picks which of the patient's appointments a new referral belongs
/// to) or from a row action on the appointments grid ([initialAppointment]
/// set, pre-fills and locks that choice for a quick add during the exam).
/// Administrator/Doctor can enter one; there is no edit, and delete
/// (Administrator-only, a soft-delete override for a mistaken entry - see
/// [ReferralProvider.delete]) is the only exception to "stays part of the
/// medical history".
///
/// Searching by reason or target specialization is a server-side filter
/// (rulebook §2.2: every list needs at least one search parameter), same
/// pattern as `PatientDocumentsScreen`.
class ReferralsScreen extends StatefulWidget {
  final int patientId;
  final String patientName;
  final Appointment? initialAppointment;

  const ReferralsScreen({
    super.key,
    required this.patientId,
    required this.patientName,
    this.initialAppointment,
  });

  @override
  State<ReferralsScreen> createState() => _ReferralsScreenState();
}

class _ReferralsScreenState extends State<ReferralsScreen> {
  late final ReferralProvider _referralProvider;
  late final SpecializationProvider _specializationProvider;
  late final AppointmentProvider _appointmentProvider;
  late final LabFindingProvider _labFindingProvider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  /// How many of the patient's most recent lab findings the details dialog
  /// surfaces, so a doctor opening a referral can see what prompted it
  /// without leaving to the full findings screen.
  static const _recentFindingsLimit = 3;

  final _searchController = TextEditingController();
  Timer? _debounce;

  bool _showArchived = false;
  List<Referral>? _referrals;
  List<Specialization> _specializations = [];
  List<Appointment> _appointments = [];
  String? _error;

  /// The term the currently-displayed list was actually fetched with - see
  /// `PatientDocumentsScreen` for why the empty state reads this instead of
  /// the live controller text.
  String _appliedSearch = '';

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _referralProvider = ReferralProvider(authSession);
    _specializationProvider = SpecializationProvider(authSession);
    _appointmentProvider = AppointmentProvider(authSession);
    _labFindingProvider = LabFindingProvider(authSession);
    _load();
  }

  Future<void> _load() async {
    final term = _searchController.text.trim();
    setState(() => _error = null);
    try {
      final results = await Future.wait([
        _referralProvider.getPaged(patientId: widget.patientId, onlyArchived: _showArchived, search: term),
        _specializationProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'}),
        _appointmentProvider.getPaged({
          'patientId': widget.patientId,
          'pageSize': 100,
          'orderBy': 'StartUtc',
          'sortDirection': 'desc',
        }),
      ]);
      if (!mounted) return;
      setState(() {
        _referrals = results[0] as List<Referral>;
        _specializations = (results[1] as dynamic).resultList as List<Specialization>;
        _appointments = (results[2] as dynamic).resultList as List<Appointment>;
        _appliedSearch = term;
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.message;
          _appliedSearch = term;
        });
      }
    }
  }

  /// Same 350ms debounce every other searchable grid in the app uses.
  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), _load);
  }

  void _clearSearch() {
    _debounce?.cancel();
    if (_searchController.text.isEmpty) return;
    _searchController.clear();
    _load();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _setShowArchived(bool value) {
    if (_showArchived == value) return;
    setState(() {
      _showArchived = value;
      _referrals = null;
    });
    _load();
  }

  Future<void> _openAddDialog() async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Nova uputnica',
          subtitle: widget.patientName,
          icon: Icons.assignment_outlined,
          width: 560,
          actions: [
            OutlinedButton(
              onPressed: isSubmitting ? null : () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: isSubmitting
                  ? null
                  : () async {
                      final form = formKey.currentState;
                      if (form == null || !form.saveAndValidate()) return;
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });
                      try {
                        await _referralProvider.create(
                          sourceAppointmentId: (form.value['appointment'] as Appointment).id,
                          targetSpecializationId: (form.value['specialization'] as Specialization).id,
                          reason: form.value['reason'] as String,
                        );
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
                        // A new referral has no reason to be hidden by a
                        // filter the user forgot about (rulebook Part II §K).
                        _debounce?.cancel();
                        _searchController.clear();
                        await _load();
                      } on ApiException catch (e) {
                        setDialogState(() {
                          fieldErrors = e.fieldErrors;
                          isSubmitting = false;
                        });
                      }
                    },
              child: isSubmitting
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sačuvaj'),
            ),
          ],
          child: FormBuilder(
            key: formKey,
            initialValue: {'appointment': widget.initialAppointment},
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Termin',
                  required: true,
                  child: FormBuilderDropdown<Appointment>(
                    name: 'appointment',
                    enabled: widget.initialAppointment == null,
                    decoration: InputDecoration(
                      hintText: 'Odaberite termin',
                      errorText: fieldErrors['sourceAppointmentId']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Termin je obavezan.'),
                    items: _appointments
                        .map(
                          (a) => DropdownMenuItem(
                            value: a,
                            child: Text('${a.medicalServiceName} - ${_dateFormat.format(a.startUtc)}'),
                          ),
                        )
                        .toList(),
                  ),
                ),
                AppField(
                  label: 'Uputiti kod specijaliste za',
                  required: true,
                  child: FormBuilderDropdown<Specialization>(
                    name: 'specialization',
                    decoration: InputDecoration(
                      hintText: 'Odaberite specijalizaciju',
                      errorText: fieldErrors['targetSpecializationId']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Specijalizacija je obavezna.'),
                    items: _specializations
                        .map((s) => DropdownMenuItem(value: s, child: Text(s.name)))
                        .toList(),
                  ),
                ),
                AppField(
                  label: 'Razlog upućivanja',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'reason',
                    maxLines: 3,
                    decoration: InputDecoration(
                      hintText: 'npr. Sumnja na aritmiju, potrebna kardiološka evaluacija.',
                      errorText: fieldErrors['reason']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Razlog upućivanja je obavezan.'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _confirmDelete(Referral referral) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Potvrda brisanja',
      message:
          'Da li ste sigurni da želite obrisati uputnicu za "${referral.targetSpecializationName}"? '
          'Ova radnja se ne može poništiti.',
      confirmLabel: 'Obriši',
      destructive: true,
    );
    if (!confirmed) return;

    try {
      await _referralProvider.delete(referral.id);
      await _load();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _downloadFinding(LabFinding finding) async {
    try {
      final response = await apiGet(
        Uri.parse(_labFindingProvider.absoluteDownloadUrl(finding)),
        headers: _labFindingProvider.authHeaders(),
        timeout: BaseProvider.fileTransferTimeout,
      );
      if (response.statusCode != 200) {
        throw Exception('HTTP ${response.statusCode}');
      }
      await FilePicker.saveFile(fileName: finding.fileName, bytes: response.bodyBytes);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Preuzimanje nije uspjelo: $e')));
      }
    }
  }

  void _openMedicalRecord(Referral referral) {
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) =>
            MedicalRecordScreen(patientId: referral.patientId, patientName: referral.patientName),
      ),
    );
  }

  /// Details popup for one referral: full context (who, why, for which
  /// specialization) plus the patient's most recent lab findings as
  /// downloadable links, so a doctor opening it understands the issue at a
  /// glance, and a shortcut into the full medical record ("medicinski
  /// karton") for anything beyond that.
  Future<void> _openDetailsDialog(Referral referral) async {
    List<LabFinding>? recentFindings;
    String? findingsError;

    try {
      final findings = await _labFindingProvider.getPaged(patientId: referral.patientId);
      recentFindings = findings.take(_recentFindingsLimit).toList();
    } on ApiException catch (e) {
      findingsError = e.message;
    }

    if (!mounted) return;

    final findings = recentFindings ?? const <LabFinding>[];

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => AppDialog(
        title: 'Detalji uputnice',
        subtitle: widget.patientName,
        icon: Icons.assignment_outlined,
        width: 560,
        actions: [
          OutlinedButton.icon(
            onPressed: () {
              Navigator.of(dialogContext).pop();
              _openMedicalRecord(referral);
            },
            icon: const Icon(Icons.folder_shared_outlined),
            label: const Text('Medicinski karton'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Zatvori'),
          ),
        ],
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _detailRow('Uputiti kod specijaliste za', referral.targetSpecializationName),
            _detailRow('Razlog upućivanja', referral.reason),
            _detailRow('Uputio', referral.referringDoctorName),
            _detailRow(
              'Termin sa kojeg je izdata',
              _dateFormat.format(referral.sourceAppointmentStartUtc.toLocal()),
            ),
            _detailRow('Datum izdavanja', _dateFormat.format(referral.createdAtUtc.toLocal())),
            Padding(
              padding: const EdgeInsets.only(top: AppSpacing.xxs),
              child: AppStatusBadge(
                label: referral.isUsed ? 'Iskorištena' : 'Aktivna',
                tone: referral.isUsed ? AppTone.success : AppTone.info,
              ),
            ),
            const SizedBox(height: AppSpacing.lg),
            AppSectionHeader(label: 'Posljednji nalazi pacijenta'),
            if (findingsError != null)
              Text(findingsError, style: TextStyle(color: dialogContext.colors.danger))
            else if (findings.isEmpty)
              Text(
                'Nema evidentiranih laboratorijskih nalaza.',
                style: TextStyle(color: dialogContext.colors.textMuted),
              )
            else
              ...findings.map(
                (finding) => Padding(
                  padding: const EdgeInsets.only(bottom: AppSpacing.xs),
                  child: AppCard(
                    padding: const EdgeInsets.all(AppSpacing.sm),
                    onTap: () => _downloadFinding(finding),
                    child: Row(
                      children: [
                        Icon(
                          finding.contentType == 'application/pdf'
                              ? Icons.picture_as_pdf_outlined
                              : Icons.image_outlined,
                          size: 18,
                          color: dialogContext.colors.primary,
                        ),
                        const SizedBox(width: AppSpacing.sm),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text(finding.result, maxLines: 2, overflow: TextOverflow.ellipsis),
                              Text(
                                '${finding.medicalServiceName} · ${_dateFormat.format(finding.appointmentStartUtc.toLocal())}',
                                style: dialogContext.text.bodySmall?.copyWith(
                                  color: dialogContext.colors.textMuted,
                                ),
                              ),
                            ],
                          ),
                        ),
                        Icon(Icons.download_outlined, size: 18, color: dialogContext.colors.textMuted),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _detailRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.sm),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 2),
          Text(value),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final canCreate = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.doctor);
    final canDelete = authSession.hasRole(Roles.administrator);
    final hasAppointmentToLinkTo = widget.initialAppointment != null || _appointments.isNotEmpty;
    final canAdd = !_showArchived && canCreate && hasAppointmentToLinkTo;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Uputnice'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).pop()),
      ),
      floatingActionButton: canAdd
          ? FloatingActionButton.extended(
              onPressed: _openAddDialog,
              icon: const Icon(Icons.add),
              label: const Text('Nova uputnica'),
            )
          : null,
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(AppSpacing.xl, AppSpacing.lg, AppSpacing.xl, 0),
            child: AppCard(
              padding: const EdgeInsets.all(AppSpacing.sm),
              child: Row(
                children: [
                  Icon(Icons.person_outline, size: 18, color: context.colors.textSecondary),
                  const SizedBox(width: AppSpacing.xs),
                  Expanded(
                    child: Text(
                      'Historija uputnica za ${widget.patientName}',
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(AppSpacing.xl, AppSpacing.md, AppSpacing.xl, 0),
            child: Align(
              alignment: Alignment.centerLeft,
              child: SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(value: false, label: Text('Aktivne')),
                  ButtonSegment(value: true, label: Text('Arhiva')),
                ],
                selected: {_showArchived},
                onSelectionChanged: (selection) => _setShowArchived(selection.first),
              ),
            ),
          ),
          if (!_showArchived && canCreate && !hasAppointmentToLinkTo)
            Padding(
              padding: const EdgeInsets.fromLTRB(AppSpacing.xl, AppSpacing.md, AppSpacing.xl, 0),
              child: AppNotice(
                tone: AppTone.warning,
                message: 'Pacijent nema nijedan termin - prvo zakažite termin da biste mogli izdati uputnicu.',
              ),
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(AppSpacing.xl, AppSpacing.md, AppSpacing.xl, 0),
            child: AppToolbar(
              filters: [
                AppSearchField(
                  controller: _searchController,
                  hint: 'Pretraži po razlogu ili specijalizaciji…',
                  onChanged: _onSearchChanged,
                  onClear: _clearSearch,
                ),
              ],
            ),
          ),
          Expanded(
            child: _error != null
                ? Padding(
                    padding: AppSpacing.page,
                    child: AppErrorState(message: _error!, onRetry: _load),
                  )
                : _referrals == null
                ? const Center(child: CircularProgressIndicator())
                : _referrals!.isEmpty
                ? Padding(
                    padding: AppSpacing.page,
                    child: _appliedSearch.isNotEmpty
                        ? AppEmptyState(
                            icon: Icons.search_off_rounded,
                            title: 'Nema rezultata pretrage',
                            message:
                                'Nijedna uputnica ne sadrži "$_appliedSearch" u razlogu ili specijalizaciji. '
                                'Provjerite pojam ili očistite pretragu da vidite sve uputnice.',
                            action: OutlinedButton.icon(
                              onPressed: _clearSearch,
                              icon: const Icon(Icons.close_rounded, size: 18),
                              label: const Text('Očisti pretragu'),
                            ),
                          )
                        : AppEmptyState(
                            icon: Icons.assignment_outlined,
                            title: _showArchived ? 'Arhiva je prazna' : 'Nema uputnica',
                            message: _showArchived
                                ? 'Uputnice se ovdje pojavljuju čim se iskoriste za zakazivanje termina, ili budu uklonjene.'
                                : 'Ovaj pacijent još nema izdatih uputnica specijalisti.',
                            action: canAdd
                                ? FilledButton.icon(
                                    onPressed: _openAddDialog,
                                    icon: const Icon(Icons.add, size: 18),
                                    label: const Text('Nova uputnica'),
                                  )
                                : null,
                          ),
                  )
                : ListView.separated(
                    padding: AppSpacing.page,
                    itemCount: _referrals!.length,
                    separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
                    itemBuilder: (context, index) {
                      final referral = _referrals![index];

                      return AppCard(
                        padding: const EdgeInsets.all(AppSpacing.sm),
                        onTap: () => _openDetailsDialog(referral),
                        child: Row(
                          children: [
                            Container(
                              width: 38,
                              height: 38,
                              alignment: Alignment.center,
                              decoration: BoxDecoration(
                                color: context.colors.primarySoft,
                                borderRadius: AppRadius.all(AppRadius.sm),
                              ),
                              child: Icon(Icons.assignment_outlined, size: 19, color: context.colors.primary),
                            ),
                            const SizedBox(width: AppSpacing.sm),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Text(
                                    'Uputnica za: ${referral.targetSpecializationName}',
                                    style: context.text.titleSmall,
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    referral.reason,
                                    style: context.text.bodySmall,
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    '${referral.referringDoctorName} · ${_dateFormat.format(referral.createdAtUtc.toLocal())}',
                                    style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ],
                              ),
                            ),
                            if (canDelete && !_showArchived)
                              AppRowAction(
                                icon: Icons.delete_outline_rounded,
                                tooltip: 'Obriši',
                                destructive: true,
                                onPressed: () => _confirmDelete(referral),
                              ),
                          ],
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
