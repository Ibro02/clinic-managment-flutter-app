import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/gender.dart';
import '../../models/patient.dart';
import '../../providers/patient_provider.dart';
import '../../core/design_tokens.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../appointments/lab_findings_screen.dart';
import '../appointments/referrals_screen.dart';
import 'archived_patients_screen.dart';
import 'medical_record_screen.dart';
import 'patient_documents_screen.dart';

class PatientScreen extends StatefulWidget {
  const PatientScreen({super.key});

  @override
  State<PatientScreen> createState() => _PatientScreenState();
}

class _PatientScreenState extends State<PatientScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Patient>>();
  late final PatientProvider _provider;
  static final _dateFormat = DateFormat('dd.MM.yyyy');

  @override
  void initState() {
    super.initState();
    _provider = PatientProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({Patient? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novi pacijent' : 'Uredi pacijenta',
          subtitle: 'Osnovni podaci pacijenta. Medicinski karton se vodi zasebno.',
          icon: Icons.person_outline_rounded,
          width: 620,
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

                      final dateOfBirth = form.value['dateOfBirth'] as DateTime?;
                      final gender = form.value['gender'] as Gender?;
                      final personalId = (form.value['personalIdNumber'] as String?)?.trim();
                      final phone = (form.value['phoneNumber'] as String?)?.trim();
                      final email = (form.value['email'] as String?)?.trim();
                      final address = (form.value['address'] as String?)?.trim();

                      final request = {
                        'firstName': form.value['firstName'],
                        'lastName': form.value['lastName'],
                        'personalIdNumber': personalId?.isEmpty == true ? null : personalId,
                        'dateOfBirth': dateOfBirth == null
                            ? null
                            : '${dateOfBirth.year.toString().padLeft(4, '0')}-${dateOfBirth.month.toString().padLeft(2, '0')}-${dateOfBirth.day.toString().padLeft(2, '0')}',
                        'gender': gender?.toInt(),
                        'phoneNumber': phone?.isEmpty == true ? null : phone,
                        'email': email?.isEmpty == true ? null : email,
                        'address': address?.isEmpty == true ? null : address,
                      };

                      try {
                        if (initial == null) {
                          await _provider.insert(request);
                        } else {
                          await _provider.update(initial.id, request);
                        }
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
              'firstName': initial?.firstName ?? '',
              'lastName': initial?.lastName ?? '',
              'personalIdNumber': initial?.personalIdNumber ?? '',
              'dateOfBirth': initial?.dateOfBirth,
              'gender': initial?.gender,
              'phoneNumber': initial?.phoneNumber ?? '',
              'email': initial?.email ?? '',
              'address': initial?.address ?? '',
            },
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                AppFormSection(
                  label: 'Identitet',
                  children: [
                    AppFieldRow(
                      children: [
                        AppField(
                          label: 'Ime',
                          required: true,
                          child: FormBuilderTextField(
                            name: 'firstName',
                            decoration: InputDecoration(errorText: fieldErrors['firstName']?.first),
                            validator: FormBuilderValidators.required(errorText: 'Ime je obavezno.'),
                          ),
                        ),
                        AppField(
                          label: 'Prezime',
                          required: true,
                          child: FormBuilderTextField(
                            name: 'lastName',
                            decoration: InputDecoration(errorText: fieldErrors['lastName']?.first),
                            validator: FormBuilderValidators.required(errorText: 'Prezime je obavezno.'),
                          ),
                        ),
                      ],
                    ),
                    AppFieldRow(
                      children: [
                        AppField(
                          label: 'JMBG / ID broj',
                          help: 'Opcionalno.',
                          child: FormBuilderTextField(
                            name: 'personalIdNumber',
                            decoration: InputDecoration(
                              hintText: '13 cifara',
                              errorText: fieldErrors['personalIdNumber']?.first,
                            ),
                          ),
                        ),
                        AppField(
                          label: 'Spol',
                          required: true,
                          child: FormBuilderDropdown<Gender>(
                            name: 'gender',
                            decoration: InputDecoration(
                              hintText: 'Odaberite',
                              errorText: fieldErrors['gender']?.first,
                            ),
                            validator: FormBuilderValidators.required(errorText: 'Spol je obavezan.'),
                            items: Gender.values
                                .map((g) => DropdownMenuItem(value: g, child: Text(g.label)))
                                .toList(),
                          ),
                        ),
                      ],
                    ),
                    AppField(
                      label: 'Datum rođenja',
                      help: 'Opcionalno.',
                      child: FormBuilderDateTimePicker(
                        name: 'dateOfBirth',
                        inputType: InputType.date,
                        format: _dateFormat,
                        decoration: const InputDecoration(hintText: 'Odaberite datum'),
                        lastDate: DateTime.now(),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.lg),
                AppFormSection(
                  label: 'Kontakt',
                  children: [
                    AppFieldRow(
                      children: [
                        AppField(
                          label: 'Broj telefona',
                          help: 'Opcionalno.',
                          child: FormBuilderTextField(
                            name: 'phoneNumber',
                            decoration: const InputDecoration(hintText: '+387 …'),
                            keyboardType: TextInputType.phone,
                          ),
                        ),
                        AppField(
                          label: 'Email',
                          help: 'Opcionalno.',
                          child: FormBuilderTextField(
                            name: 'email',
                            decoration: InputDecoration(
                              hintText: 'ime@domena.com',
                              errorText: fieldErrors['email']?.first,
                            ),
                            keyboardType: TextInputType.emailAddress,
                            validator: FormBuilderValidators.email(
                              errorText: 'Unesite ispravnu email adresu (npr. ime@domena.com).',
                            ),
                          ),
                        ),
                      ],
                    ),
                    AppField(
                      label: 'Adresa',
                      help: 'Opcionalno.',
                      child: FormBuilderTextField(
                        name: 'address',
                        decoration: const InputDecoration(hintText: 'Ulica i broj, grad'),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );

    _tableKey.currentState?.load();
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final canWrite = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    return PagedCodebookTable<Patient>(
      key: _tableKey,
      title: 'Pacijenti',
      searchHint: 'Pretraga po imenu i prezimenu',
      provider: _provider,
      orderBy: 'LastName',
      canWrite: canWrite,
      extraActions: [
        if (canWrite)
          OutlinedButton.icon(
            onPressed: () async {
              await Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ArchivedPatientsScreen()));
              _tableKey.currentState?.load();
            },
            icon: const Icon(Icons.inventory_2_outlined, size: 18),
            label: const Text('Arhivirani pacijenti'),
          ),
      ],
      buildColumns: () => [
        AppColumn(
          label: 'Ime i prezime',
          sortKey: 'LastName',
          flex: 2,
          cell: (context, patient) => Row(
            children: [
              AppAvatar(name: patient.fullName, size: 30),
              const SizedBox(width: AppSpacing.xs),
              Expanded(child: Text(patient.fullName, overflow: TextOverflow.ellipsis)),
            ],
          ),
        ),
        AppColumn(
          label: 'JMBG / ID',
          width: 150,
          numeric: true,
          cell: (context, patient) => Text(patient.personalIdNumber ?? '—'),
        ),
        AppColumn(
          label: 'Datum rođenja',
          width: 140,
          numeric: true,
          cell: (context, patient) =>
              Text(patient.dateOfBirth == null ? '—' : _dateFormat.format(patient.dateOfBirth!)),
        ),
        AppColumn(
          label: 'Telefon',
          width: 150,
          numeric: true,
          cell: (context, patient) => Text(patient.phoneNumber ?? '—'),
        ),
      ],
      extraRowActions: (patient) => [
        AppRowAction(
          icon: Icons.folder_shared_outlined,
          tooltip: 'Medicinski karton',
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute(
              builder: (_) => MedicalRecordScreen(patientId: patient.id, patientName: patient.fullName),
            ),
          ),
        ),
        AppRowAction(
          icon: Icons.folder_outlined,
          tooltip: 'Dokumenti',
          onPressed: () => Navigator.of(
            context,
          ).push(MaterialPageRoute(builder: (_) => PatientDocumentsScreen(patient: patient))),
        ),
        AppRowAction(
          icon: Icons.biotech_outlined,
          tooltip: 'Laboratorijski nalazi',
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute(
              builder: (_) => LabFindingsScreen(patientId: patient.id, patientName: patient.fullName),
            ),
          ),
        ),
        AppRowAction(
          icon: Icons.assignment_outlined,
          tooltip: 'Uputnice',
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute(
              builder: (_) => ReferralsScreen(patientId: patient.id, patientName: patient.fullName),
            ),
          ),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (patient) => _openForm(initial: patient),
      onDelete: (patient) => _provider.delete(patient.id),
      itemLabel: (patient) => patient.fullName,
      addLabel: 'Dodaj pacijenta',
    );
  }
}
