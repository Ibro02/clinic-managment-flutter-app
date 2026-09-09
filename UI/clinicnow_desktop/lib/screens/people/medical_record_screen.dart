import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/diagnosis.dart';
import '../../models/medical_record.dart';
import '../../providers/diagnosis_provider.dart';
import '../../providers/medical_record_provider.dart';
import '../../core/design_tokens.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

/// The medical file ("medicinski karton") editor/viewer: clinic header, basic
/// patient info, allergies/notes, and the treatment-history table. A Doctor
/// can only *add* content (append notes, add a table row) - never edit or
/// delete anything already applied to the file; an Administrator has full
/// CRUD over the same content. Every other role sees a read-only view.
class MedicalRecordScreen extends StatefulWidget {
  final int patientId;
  final String patientName;

  const MedicalRecordScreen({super.key, required this.patientId, required this.patientName});

  @override
  State<MedicalRecordScreen> createState() => _MedicalRecordScreenState();
}

class _MedicalRecordScreenState extends State<MedicalRecordScreen> {
  late final MedicalRecordProvider _provider;
  late final DiagnosisProvider _diagnosisProvider;
  final _dateFormat = DateFormat('dd.MM.yyyy');

  MedicalRecord? _record;
  String? _error;

  /// The diagnosis codebook, loaded once and reused by every open of the entry
  /// dialog - the dropdown is populated from the database, never typed as free
  /// text (rulebook §6, prijava §4.1).
  List<Diagnosis> _diagnoses = [];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _provider = MedicalRecordProvider(authSession);
    _diagnosisProvider = DiagnosisProvider(authSession);
    _load();
    _loadDiagnoses();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final record = await _provider.getByPatientId(widget.patientId);
      if (mounted) setState(() => _record = record);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _loadDiagnoses() async {
    try {
      final diagnoses = await _diagnosisProvider.getAllForDropdown();
      if (mounted) setState(() => _diagnoses = diagnoses);
    } on ApiException {
      // Leaves _diagnoses empty; _openEntryForm refuses to open in that case
      // with an explanation, rather than showing an empty dropdown
      // (rulebook §6: don't open an add-form when its preconditions fail).
    }
  }

  Future<void> _openAppendNotesDialog() async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Dodaj u alergije / napomene',
          subtitle: 'Novi tekst se dopisuje uz postojeći, ništa se ne briše.',
          icon: Icons.note_add_outlined,
          width: 520,
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
                      setDialogState(() => isSubmitting = true);
                      try {
                        final record = await _provider.appendNotes(
                          widget.patientId,
                          allergiesToAppend: form.value['allergiesToAppend'] as String?,
                          medicalNotesToAppend: form.value['medicalNotesToAppend'] as String?,
                        );
                        if (mounted) setState(() => _record = record);
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
                      } on ApiException catch (e) {
                        setDialogState(() => isSubmitting = false);
                        if (dialogContext.mounted) {
                          ScaffoldMessenger.of(
                            dialogContext,
                          ).showSnackBar(SnackBar(content: Text(e.message)));
                        }
                      }
                    },
              child: isSubmitting
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Dodaj'),
            ),
          ],
          child: FormBuilder(
            key: formKey,
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Dodaj alergiju',
                  help: 'Opcionalno.',
                  child: FormBuilderTextField(
                    name: 'allergiesToAppend',
                    decoration: const InputDecoration(hintText: 'npr. Penicilin'),
                  ),
                ),
                AppField(
                  label: 'Dodaj napomenu',
                  help: 'Opcionalno.',
                  child: FormBuilderTextField(
                    name: 'medicalNotesToAppend',
                    maxLines: 3,
                    decoration: const InputDecoration(hintText: 'Napomena za karton'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _openReplaceNotesDialog() async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Uredi alergije / napomene',
          subtitle: 'Ovim se postojeći tekst zamjenjuje u cijelosti.',
          icon: Icons.edit_note_outlined,
          tone: AppTone.warning,
          width: 520,
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
                      setDialogState(() => isSubmitting = true);
                      try {
                        final record = await _provider.replaceNotes(
                          widget.patientId,
                          allergies: form.value['allergies'] as String?,
                          medicalNotes: form.value['medicalNotes'] as String?,
                        );
                        if (mounted) setState(() => _record = record);
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
                      } on ApiException catch (e) {
                        setDialogState(() => isSubmitting = false);
                        if (dialogContext.mounted) {
                          ScaffoldMessenger.of(
                            dialogContext,
                          ).showSnackBar(SnackBar(content: Text(e.message)));
                        }
                      }
                    },
              child: isSubmitting
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sačuvaj'),
            ),
          ],
          child: FormBuilder(
            key: formKey,
            initialValue: {
              'allergies': _record?.allergies ?? '',
              'medicalNotes': _record?.medicalNotes ?? '',
            },
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Alergije',
                  child: FormBuilderTextField(name: 'allergies'),
                ),
                AppField(
                  label: 'Napomene',
                  child: FormBuilderTextField(name: 'medicalNotes', maxLines: 4),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _openEntryForm({MedicalRecordEntry? initial}) async {
    // The diagnosis dropdown is the entry's required field, so an empty
    // codebook means the form cannot be completed - explain that instead of
    // opening a dead form (rulebook §6).
    if (_diagnoses.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Šifrarnik dijagnoza je prazan ili nije učitan. '
            'Dodajte dijagnoze pod Šifrarnici → Dijagnoze prije upisa u karton.',
          ),
        ),
      );
      return;
    }

    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novi unos u historiju liječenja' : 'Uredi unos',
          subtitle: widget.patientName,
          icon: Icons.medical_information_outlined,
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
                        final diagnosisId = (form.value['diagnosis'] as Diagnosis).id;
                        final diagnosisNote = (form.value['diagnosisNote'] as String?)?.trim();
                        final record = initial == null
                            ? await _provider.addEntry(
                                widget.patientId,
                                entryDate: form.value['entryDate'] as DateTime,
                                diagnosisId: diagnosisId,
                                diagnosisNote: diagnosisNote?.isEmpty == true ? null : diagnosisNote,
                                treatment: form.value['treatment'] as String,
                                description: form.value['description'] as String,
                              )
                            : await _provider.updateEntry(
                                initial.id,
                                entryDate: form.value['entryDate'] as DateTime,
                                diagnosisId: diagnosisId,
                                diagnosisNote: diagnosisNote?.isEmpty == true ? null : diagnosisNote,
                                treatment: form.value['treatment'] as String,
                                description: form.value['description'] as String,
                              );
                        if (mounted) setState(() => _record = record);
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
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
            initialValue: {
              'entryDate': initial?.entryDate ?? DateTime.now(),
              'diagnosis': _diagnoses.where((d) => d.id == initial?.diagnosisId).firstOrNull,
              'diagnosisNote': initial?.diagnosisNote ?? '',
              'treatment': initial?.treatment ?? '',
              'description': initial?.description ?? '',
            },
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Datum',
                  required: true,
                  child: FormBuilderDateTimePicker(
                    name: 'entryDate',
                    inputType: InputType.date,
                    format: _dateFormat,
                    lastDate: DateTime.now(),
                    validator: FormBuilderValidators.required(errorText: 'Datum je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Dijagnoza',
                  required: true,
                  help: 'Bira se iz MKB-10 šifrarnika, ne upisuje se slobodno.',
                  child: FormBuilderDropdown<Diagnosis>(
                    name: 'diagnosis',
                    isExpanded: true,
                    decoration: InputDecoration(
                      hintText: 'Odaberite dijagnozu',
                      errorText: fieldErrors['diagnosisId']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Dijagnoza je obavezna.'),
                    items: _diagnoses
                        .map(
                          (d) => DropdownMenuItem(
                            value: d,
                            child: Text(d.displayName, overflow: TextOverflow.ellipsis),
                          ),
                        )
                        .toList(),
                  ),
                ),
                AppField(
                  label: 'Napomena uz dijagnozu',
                  help: 'Opcionalno. Npr. zahvaćena strana, recidiv.',
                  child: FormBuilderTextField(
                    name: 'diagnosisNote',
                    decoration: InputDecoration(
                      hintText: 'npr. lijeva strana, drugi recidiv',
                      errorText: fieldErrors['diagnosisNote']?.first,
                    ),
                  ),
                ),
                AppField(
                  label: 'Tretman',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'treatment',
                    decoration: InputDecoration(
                      hintText: 'npr. Kontrolni pregled',
                      errorText: fieldErrors['treatment']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Tretman je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Opis',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'description',
                    maxLines: 4,
                    decoration: InputDecoration(errorText: fieldErrors['description']?.first),
                    validator: FormBuilderValidators.required(errorText: 'Opis je obavezan.'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _confirmDeleteEntry(MedicalRecordEntry entry) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Potvrda brisanja',
      message:
          'Da li ste sigurni da želite obrisati unos "${entry.treatment}" '
          'od ${_dateFormat.format(entry.entryDate)}? Ova radnja se ne može poništiti.',
      confirmLabel: 'Obriši',
      destructive: true,
    );
    if (!confirmed) return;

    try {
      await _provider.deleteEntry(entry.id);
      await _load();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final isDoctor = authSession.hasRole(Roles.doctor);
    final isAdmin = authSession.hasRole(Roles.administrator);
    // A Doctor can only ever add content (append notes, add a history row);
    // an Administrator gets full CRUD over the same content (explicit
    // requirement) - every other role (Staff, Patient viewing their own) is
    // strictly read-only here.
    final canAppend = isDoctor || isAdmin;
    final canFullyEdit = isAdmin;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Medicinski karton'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).pop()),
      ),
      floatingActionButton: canAppend && _record != null
          ? FloatingActionButton.extended(
              onPressed: () => _openEntryForm(),
              icon: const Icon(Icons.add),
              label: const Text('Novi unos'),
            )
          : null,
      body: _error != null
          ? Padding(
              padding: AppSpacing.page,
              child: AppErrorState(message: _error!, onRetry: _load),
            )
          : _record == null
          ? const Center(child: CircularProgressIndicator())
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: AppSpacing.page,
                children: [
                  _buildHeader(context),
                  const Divider(height: 32, thickness: 1.5),
                  _buildBasicInfo(context),
                  const SizedBox(height: 24),
                  _buildNotesSection(context, canAppend: canAppend, canFullyEdit: canFullyEdit),
                  const SizedBox(height: 24),
                  _buildEntriesTable(context, canAppend: canAppend, canFullyEdit: canFullyEdit),
                ],
              ),
            ),
    );
  }

  Widget _buildHeader(BuildContext context) {
    // No real clinic logo asset exists in this project yet - a stylized icon
    // stands in for it (rulebook §6 asks for a logo in the header; swapping
    // in a real image asset later is a one-line change here).
    return Row(
      children: [
        CircleAvatar(
          radius: 28,
          backgroundColor: Theme.of(context).colorScheme.primaryContainer,
          child: Icon(
            Icons.local_hospital,
            size: 32,
            color: Theme.of(context).colorScheme.onPrimaryContainer,
          ),
        ),
        const SizedBox(width: 16),
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('ClinicNow', style: Theme.of(context).textTheme.headlineSmall),
            Text('Medicinski karton pacijenta', style: Theme.of(context).textTheme.bodyMedium),
          ],
        ),
      ],
    );
  }

  Widget _buildBasicInfo(BuildContext context) {
    final record = _record!;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Osnovni podaci', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),
            _infoRow('Ime i prezime', '${record.patientFirstName} ${record.patientLastName}'),
            _infoRow('Starost', record.patientAge == null ? '—' : '${record.patientAge} godina'),
            _infoRow('Spol', record.patientGender?.label ?? '—'),
            _infoRow('Adresa', record.patientAddress ?? '—'),
            _infoRow('Email', record.patientEmail ?? '—'),
            _infoRow('Telefon', record.patientPhoneNumber ?? '—'),
          ],
        ),
      ),
    );
  }

  Widget _infoRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          SizedBox(
            width: 160,
            child: Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
          ),
          Expanded(child: Text(value)),
        ],
      ),
    );
  }

  Widget _buildNotesSection(BuildContext context, {required bool canAppend, required bool canFullyEdit}) {
    final record = _record!;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text('Alergije i napomene', style: Theme.of(context).textTheme.titleMedium)),
                if (canFullyEdit)
                  TextButton.icon(
                    onPressed: _openReplaceNotesDialog,
                    icon: const Icon(Icons.edit_outlined),
                    label: const Text('Uredi'),
                  ),
                if (canAppend)
                  TextButton.icon(
                    onPressed: _openAppendNotesDialog,
                    icon: const Icon(Icons.add),
                    label: const Text('Dodaj'),
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text('Alergije:', style: Theme.of(context).textTheme.labelLarge),
            Text(record.allergies?.isNotEmpty == true ? record.allergies! : 'Nema evidentiranih alergija.'),
            const SizedBox(height: 12),
            Text('Napomene:', style: Theme.of(context).textTheme.labelLarge),
            Text(record.medicalNotes?.isNotEmpty == true ? record.medicalNotes! : 'Nema napomena.'),
          ],
        ),
      ),
    );
  }

  Widget _buildEntriesTable(BuildContext context, {required bool canAppend, required bool canFullyEdit}) {
    final entries = _record!.entries;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Historija liječenja', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),
            if (entries.isEmpty)
              const Text('Nema unosa u historiji liječenja.')
            else
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: DataTable(
                  columns: const [
                    DataColumn(label: Text('Datum')),
                    DataColumn(label: Text('Dijagnoza')),
                    DataColumn(label: Text('Tretman')),
                    DataColumn(label: Text('Opis')),
                    DataColumn(label: Text('Unio/la')),
                    DataColumn(label: Text('Akcije')),
                  ],
                  rows: entries
                      .map(
                        (entry) => DataRow(
                          cells: [
                            DataCell(Text(_dateFormat.format(entry.entryDate))),
                            DataCell(
                              SizedBox(
                                width: 220,
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  children: [
                                    Text(entry.diagnosisDisplayName, overflow: TextOverflow.ellipsis),
                                    if ((entry.diagnosisNote ?? '').isNotEmpty)
                                      Text(
                                        entry.diagnosisNote!,
                                        style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
                                        overflow: TextOverflow.ellipsis,
                                      ),
                                  ],
                                ),
                              ),
                            ),
                            DataCell(Text(entry.treatment)),
                            DataCell(SizedBox(width: 280, child: Text(entry.description))),
                            DataCell(Text(entry.createdByName)),
                            DataCell(
                              canFullyEdit
                                  ? Row(
                                      mainAxisSize: MainAxisSize.min,
                                      children: [
                                        IconButton(
                                          tooltip: 'Uredi',
                                          icon: const Icon(Icons.edit_outlined),
                                          onPressed: () => _openEntryForm(initial: entry),
                                        ),
                                        IconButton(
                                          tooltip: 'Obriši',
                                          icon: const Icon(Icons.delete_outline),
                                          onPressed: () => _confirmDeleteEntry(entry),
                                        ),
                                      ],
                                    )
                                  : const SizedBox.shrink(),
                            ),
                          ],
                        ),
                      )
                      .toList(),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
