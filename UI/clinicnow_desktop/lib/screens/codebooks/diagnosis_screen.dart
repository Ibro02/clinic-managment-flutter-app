import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/diagnosis.dart';
import '../../models/specialization.dart';
import '../../providers/diagnosis_provider.dart';
import '../../providers/specialization_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

/// The ICD-10 diagnosis codebook. A medical-record entry references a row from
/// here instead of storing free text (prijava §4.1), which is why this needs
/// full CRUD like every other reference table (rulebook §2.2).
class DiagnosisScreen extends StatefulWidget {
  const DiagnosisScreen({super.key});

  @override
  State<DiagnosisScreen> createState() => _DiagnosisScreenState();
}

class _DiagnosisScreenState extends State<DiagnosisScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Diagnosis>>();
  late final DiagnosisProvider _provider;
  late final SpecializationProvider _specializationProvider;

  List<Specialization> _specializations = [];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _provider = DiagnosisProvider(authSession);
    _specializationProvider = SpecializationProvider(authSession);
    _loadSpecializations();
  }

  Future<void> _loadSpecializations() async {
    try {
      final result = await _specializationProvider.getPaged({'page': 1, 'pageSize': 100, 'orderBy': 'Name'});
      if (mounted) setState(() => _specializations = result.resultList);
    } on ApiException {
      // Best-effort: the specialization link is optional, so the form still
      // works without it rather than blocking the whole screen.
    }
  }

  Future<void> _openForm({Diagnosis? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Nova dijagnoza' : 'Uredi dijagnozu',
          subtitle: 'Dijagnoze se biraju pri upisu u medicinski karton.',
          icon: Icons.coronavirus_outlined,
          width: 480,
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
                      final request = {
                        'code': (form.value['code'] as String).trim(),
                        'name': (form.value['name'] as String).trim(),
                        'suggestedSpecializationId': (form.value['specialization'] as Specialization?)?.id,
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
              'code': initial?.code ?? '',
              'name': initial?.name ?? '',
              'specialization': _specializations
                  .where((s) => s.id == initial?.suggestedSpecializationId)
                  .firstOrNull,
            },
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Šifra (MKB-10)',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'code',
                    decoration: InputDecoration(
                      hintText: 'npr. J06.9',
                      errorText: fieldErrors['code']?.first,
                    ),
                    validator: FormBuilderValidators.compose([
                      FormBuilderValidators.required(errorText: 'Šifra dijagnoze je obavezna, npr. J06.9.'),
                      FormBuilderValidators.maxLength(10, errorText: 'Šifra može imati najviše 10 znakova.'),
                    ]),
                  ),
                ),
                AppField(
                  label: 'Naziv',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'name',
                    decoration: InputDecoration(
                      hintText: 'npr. Akutna infekcija gornjih disajnih puteva',
                      errorText: fieldErrors['name']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Naziv dijagnoze je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Specijalizacija',
                  help: 'Opcionalno. Predlaže se pri izdavanju uputnice za ovu dijagnozu.',
                  child: FormBuilderDropdown<Specialization>(
                    name: 'specialization',
                    decoration: InputDecoration(
                      hintText: 'Bez specijalizacije',
                      errorText: fieldErrors['suggestedSpecializationId']?.first,
                    ),
                    items: _specializations
                        .map((s) => DropdownMenuItem(value: s, child: Text(s.name)))
                        .toList(),
                  ),
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
    return PagedCodebookTable<Diagnosis>(
      key: _tableKey,
      title: 'Dijagnoze',
      subtitle: 'MKB-10 šifrarnik koji se koristi pri upisu u medicinski karton.',
      searchHint: 'Pretraga po šifri ili nazivu',
      // The backend filters on either the code or the name, not just `name`.
      searchParam: 'search',
      orderBy: 'Code',
      provider: _provider,
      buildColumns: () => [
        AppColumn(
          label: 'Šifra',
          sortKey: 'Code',
          flex: 1,
          cell: (context, diagnosis) => Text(diagnosis.code),
        ),
        AppColumn(
          label: 'Naziv',
          sortKey: 'Name',
          flex: 3,
          cell: (context, diagnosis) => Text(diagnosis.name),
        ),
        AppColumn(
          label: 'Specijalizacija',
          flex: 2,
          cell: (context, diagnosis) => Text(diagnosis.suggestedSpecializationName ?? '—'),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (diagnosis) => _openForm(initial: diagnosis),
      onDelete: (diagnosis) => _provider.delete(diagnosis.id),
      itemLabel: (diagnosis) => diagnosis.displayName,
    );
  }
}
