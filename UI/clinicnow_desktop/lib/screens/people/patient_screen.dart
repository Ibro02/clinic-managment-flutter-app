import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/patient.dart';
import '../../providers/patient_provider.dart';
import '../../widgets/paged_codebook_table.dart';

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

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Novi pacijent' : 'Uredi pacijenta'),
          content: SizedBox(
            width: 460,
            child: FormBuilder(
              key: formKey,
              initialValue: {
                'firstName': initial?.firstName ?? '',
                'lastName': initial?.lastName ?? '',
                'personalIdNumber': initial?.personalIdNumber ?? '',
                'dateOfBirth': initial?.dateOfBirth,
                'phoneNumber': initial?.phoneNumber ?? '',
                'address': initial?.address ?? '',
              },
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: FormBuilderTextField(
                          name: 'firstName',
                          decoration: InputDecoration(labelText: 'Ime', errorText: fieldErrors['firstName']?.first),
                          validator: FormBuilderValidators.required(errorText: 'Ime je obavezno.'),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: FormBuilderTextField(
                          name: 'lastName',
                          decoration: InputDecoration(labelText: 'Prezime', errorText: fieldErrors['lastName']?.first),
                          validator: FormBuilderValidators.required(errorText: 'Prezime je obavezno.'),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'personalIdNumber',
                    decoration: InputDecoration(
                      labelText: 'JMBG / ID broj (opcionalno)',
                      errorText: fieldErrors['personalIdNumber']?.first,
                    ),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderDateTimePicker(
                    name: 'dateOfBirth',
                    inputType: InputType.date,
                    format: _dateFormat,
                    decoration: const InputDecoration(labelText: 'Datum rođenja (opcionalno)'),
                    lastDate: DateTime.now(),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'phoneNumber',
                    decoration: const InputDecoration(labelText: 'Broj telefona (opcionalno)'),
                    keyboardType: TextInputType.phone,
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'address',
                    decoration: const InputDecoration(labelText: 'Adresa (opcionalno)'),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
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
                      final personalId = (form.value['personalIdNumber'] as String?)?.trim();
                      final phone = (form.value['phoneNumber'] as String?)?.trim();
                      final address = (form.value['address'] as String?)?.trim();

                      final request = {
                        'firstName': form.value['firstName'],
                        'lastName': form.value['lastName'],
                        'personalIdNumber': personalId?.isEmpty == true ? null : personalId,
                        'dateOfBirth': dateOfBirth == null
                            ? null
                            : '${dateOfBirth.year.toString().padLeft(4, '0')}-${dateOfBirth.month.toString().padLeft(2, '0')}-${dateOfBirth.day.toString().padLeft(2, '0')}',
                        'phoneNumber': phone?.isEmpty == true ? null : phone,
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
      buildColumns: () => const [
        DataColumn(label: Text('Ime i prezime')),
        DataColumn(label: Text('JMBG / ID')),
        DataColumn(label: Text('Datum rođenja')),
        DataColumn(label: Text('Telefon')),
      ],
      buildCells: (patient) => [
        DataCell(Text(patient.fullName)),
        DataCell(Text(patient.personalIdNumber ?? '—')),
        DataCell(Text(patient.dateOfBirth == null ? '—' : _dateFormat.format(patient.dateOfBirth!))),
        DataCell(Text(patient.phoneNumber ?? '—')),
      ],
      onAdd: () => _openForm(),
      onEdit: (patient) => _openForm(initial: patient),
      onDelete: (patient) => _provider.delete(patient.id),
      itemLabel: (patient) => patient.fullName,
    );
  }
}
