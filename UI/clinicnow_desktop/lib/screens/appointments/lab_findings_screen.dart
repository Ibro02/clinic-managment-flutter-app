import '../../core/base_provider.dart';
import '../../core/api_http.dart';
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
import '../../providers/appointment_provider.dart';
import '../../providers/lab_finding_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

/// Entry and review of a patient's lab findings ("laboratorijski nalazi",
/// review item C4). Reached either from a row action on the patients grid
/// (patient-wide management: [initialAppointment] null, the doctor picks
/// which of the patient's appointments a new finding belongs to) or from a
/// row action on the appointments grid ([initialAppointment] set, pre-fills
/// and locks that choice for a quick add during the exam). Either way, every
/// finding is still tied to a specific appointment server-side - only the UI
/// entry point differs. Administrator/Staff/Doctor can enter a finding;
/// deleting is Administrator/Staff only, mirroring `PatientDocumentsScreen`'s
/// gates for the equivalent generic-document flow.
class LabFindingsScreen extends StatefulWidget {
  final int patientId;
  final String patientName;
  final Appointment? initialAppointment;

  const LabFindingsScreen({
    super.key,
    required this.patientId,
    required this.patientName,
    this.initialAppointment,
  });

  @override
  State<LabFindingsScreen> createState() => _LabFindingsScreenState();
}

class _LabFindingsScreenState extends State<LabFindingsScreen> {
  late final LabFindingProvider _provider;
  late final AppointmentProvider _appointmentProvider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  List<LabFinding>? _findings;
  List<Appointment> _appointments = [];
  String? _error;

  static const _allowedExtensions = ['pdf', 'png', 'jpg', 'jpeg'];
  static const _contentTypeByExtension = {
    'pdf': 'application/pdf',
    'png': 'image/png',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
  };

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _provider = LabFindingProvider(authSession);
    _appointmentProvider = AppointmentProvider(authSession);
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final results = await Future.wait([
        _provider.getPaged(patientId: widget.patientId),
        _appointmentProvider.getPaged({
          'patientId': widget.patientId,
          'pageSize': 100,
          'orderBy': 'StartUtc',
          'sortDirection': 'desc',
        }),
      ]);
      if (!mounted) return;
      setState(() {
        _findings = results[0] as List<LabFinding>;
        _appointments = (results[1] as dynamic).resultList as List<Appointment>;
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _openAddDialog() async {
    final formKey = GlobalKey<FormBuilderState>();
    PlatformFile? pickedFile;
    var isSubmitting = false;
    String? fileError;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: 'Novi laboratorijski nalaz',
          subtitle: widget.patientName,
          icon: Icons.biotech_outlined,
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
                      if (pickedFile == null) {
                        setDialogState(() => fileError = 'Fajl je obavezan.');
                        return;
                      }

                      final extension = pickedFile!.extension?.toLowerCase() ?? '';
                      final contentType = _contentTypeByExtension[extension];
                      if (contentType == null) {
                        setDialogState(() => fileError = 'Dozvoljeni su samo PDF, PNG i JPEG fajlovi.');
                        return;
                      }

                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });

                      try {
                        final bytes = await pickedFile!.readAsBytes();
                        await _provider.create(
                          appointmentId: (form.value['appointment'] as Appointment).id,
                          result: form.value['result'] as String,
                          fileName: pickedFile!.name,
                          contentType: contentType,
                          bytes: bytes,
                        );
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
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
                      errorText: fieldErrors['appointmentId']?.first,
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
                  label: 'Nalaz',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'result',
                    maxLines: 3,
                    decoration: InputDecoration(
                      hintText: 'npr. Kompletna krvna slika - uredni parametri.',
                      errorText: fieldErrors['result']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Nalaz je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Fajl',
                  required: true,
                  help: 'PDF, PNG ili JPEG.',
                  child: OutlinedButton.icon(
                    onPressed: () async {
                      final result = await FilePicker.pickFiles(
                        type: FileType.custom,
                        allowedExtensions: _allowedExtensions,
                      );
                      if (result.isEmpty) return;
                      setDialogState(() {
                        pickedFile = result.single;
                        fileError = null;
                      });
                    },
                    icon: const Icon(Icons.attach_file),
                    label: Text(pickedFile?.name ?? 'Odaberi fajl'),
                  ),
                ),
                if (fileError != null)
                  Padding(
                    padding: const EdgeInsets.only(top: AppSpacing.xxs),
                    child: Text(fileError!, style: TextStyle(color: dialogContext.colors.danger, fontSize: 12)),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _download(LabFinding finding) async {
    try {
      final response = await apiGet(
        Uri.parse(_provider.absoluteDownloadUrl(finding)),
        headers: _provider.authHeaders(),
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

  Future<void> _confirmDelete(LabFinding finding) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Potvrda brisanja',
      message: 'Da li ste sigurni da želite obrisati ovaj nalaz? Ova radnja se ne može poništiti.',
      confirmLabel: 'Obriši',
      destructive: true,
    );
    if (!confirmed) return;

    try {
      await _provider.delete(finding.id);
      await _load();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final canEnter =
        authSession.hasRole(Roles.administrator) ||
        authSession.hasRole(Roles.staff) ||
        authSession.hasRole(Roles.doctor);
    final canDelete = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);
    // Rulebook Part II §K: don't open an add form when its precondition (a
    // patient's own appointment to link the finding to) is missing - only
    // relevant when reached from the patients grid, since the appointments
    // grid always supplies its own appointment already.
    final canAdd = canEnter && (widget.initialAppointment != null || _appointments.isNotEmpty);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Laboratorijski nalazi'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).pop()),
      ),
      floatingActionButton: canAdd
          ? FloatingActionButton.extended(
              onPressed: _openAddDialog,
              icon: const Icon(Icons.add),
              label: const Text('Novi nalaz'),
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
                  Icon(Icons.event_outlined, size: 18, color: context.colors.textSecondary),
                  const SizedBox(width: AppSpacing.xs),
                  Expanded(
                    child: Text(
                      widget.initialAppointment == null
                          ? 'Historija laboratorijskih nalaza za ${widget.patientName}'
                          : '${widget.initialAppointment!.patientName} - ${widget.initialAppointment!.medicalServiceName} kod '
                                '${widget.initialAppointment!.doctorName} '
                                '(${_dateFormat.format(widget.initialAppointment!.startUtc)})',
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
            ),
          ),
          if (canEnter && !canAdd)
            Padding(
              padding: const EdgeInsets.fromLTRB(AppSpacing.xl, AppSpacing.md, AppSpacing.xl, 0),
              child: AppNotice(
                tone: AppTone.warning,
                message: 'Pacijent nema nijedan termin - prvo zakažite termin da biste mogli unijeti nalaz.',
              ),
            ),
          Expanded(
            child: _error != null
                ? Padding(
                    padding: AppSpacing.page,
                    child: AppErrorState(message: _error!, onRetry: _load),
                  )
                : _findings == null
                ? const Center(child: CircularProgressIndicator())
                : _findings!.isEmpty
                ? Padding(
                    padding: AppSpacing.page,
                    child: AppEmptyState(
                      icon: Icons.biotech_outlined,
                      title: 'Nema nalaza',
                      message: 'Ovaj pacijent još nema nijedan laboratorijski nalaz.',
                      action: canAdd
                          ? FilledButton.icon(
                              onPressed: _openAddDialog,
                              icon: const Icon(Icons.add, size: 18),
                              label: const Text('Novi nalaz'),
                            )
                          : null,
                    ),
                  )
                : ListView.separated(
                    padding: AppSpacing.page,
                    itemCount: _findings!.length,
                    separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
                    itemBuilder: (context, index) {
                      final finding = _findings![index];
                      final isPdf = finding.contentType == 'application/pdf';

                      return AppCard(
                        padding: const EdgeInsets.all(AppSpacing.sm),
                        child: Row(
                          children: [
                            Container(
                              width: 38,
                              height: 38,
                              alignment: Alignment.center,
                              decoration: BoxDecoration(
                                color: (isPdf ? AppTone.danger : AppTone.info).background(context),
                                borderRadius: AppRadius.all(AppRadius.sm),
                              ),
                              child: Icon(
                                isPdf ? Icons.picture_as_pdf_outlined : Icons.image_outlined,
                                size: 19,
                                color: (isPdf ? AppTone.danger : AppTone.info).foreground(context),
                              ),
                            ),
                            const SizedBox(width: AppSpacing.sm),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Text(finding.result, style: context.text.titleSmall),
                                  const SizedBox(height: 2),
                                  Text(
                                    '${finding.medicalServiceName} (${_dateFormat.format(finding.appointmentStartUtc.toLocal())}) · '
                                    '${finding.fileName} · ${finding.enteredByName}',
                                    style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ],
                              ),
                            ),
                            const SizedBox(width: AppSpacing.xs),
                            AppRowAction(
                              icon: Icons.download_outlined,
                              tooltip: 'Preuzmi',
                              onPressed: () => _download(finding),
                            ),
                            if (canDelete)
                              AppRowAction(
                                icon: Icons.delete_outline_rounded,
                                tooltip: 'Obriši',
                                destructive: true,
                                onPressed: () => _confirmDelete(finding),
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
