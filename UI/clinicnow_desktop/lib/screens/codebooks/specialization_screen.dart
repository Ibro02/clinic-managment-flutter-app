import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/specialization.dart';
import '../../providers/specialization_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

class SpecializationScreen extends StatefulWidget {
  const SpecializationScreen({super.key});

  @override
  State<SpecializationScreen> createState() => _SpecializationScreenState();
}

class _SpecializationScreenState extends State<SpecializationScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Specialization>>();
  late final SpecializationProvider _provider;

  @override
  void initState() {
    super.initState();
    _provider = SpecializationProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({Specialization? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Nova specijalizacija' : 'Uredi specijalizaciju',
          subtitle: 'Specijalizacije se dodjeljuju doktorima.',
          icon: Icons.medical_services_outlined,
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
                      final description = (form.value['description'] as String?)?.trim();
                      final request = {
                        'name': form.value['name'],
                        'description': description?.isEmpty == true ? null : description,
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
            initialValue: {'name': initial?.name ?? '', 'description': initial?.description ?? ''},
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Naziv',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'name',
                    decoration: InputDecoration(
                      hintText: 'npr. Dermatologija',
                      errorText: fieldErrors['name']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Naziv je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Opis',
                  help: 'Opcionalno. Kratko objašnjenje za osoblje.',
                  child: FormBuilderTextField(
                    name: 'description',
                    decoration: const InputDecoration(hintText: 'Kratak opis specijalizacije'),
                    maxLines: 3,
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
    return PagedCodebookTable<Specialization>(
      key: _tableKey,
      title: 'Specijalizacije',
      searchHint: 'Pretraga po nazivu',
      provider: _provider,
      buildColumns: () => [
        AppColumn(
          label: 'Naziv',
          sortKey: 'Name',
          flex: 2,
          cell: (context, specialization) => Text(specialization.name),
        ),
        AppColumn(
          label: 'Opis',
          flex: 3,
          cell: (context, specialization) =>
              Text(specialization.description?.isNotEmpty == true ? specialization.description! : '—'),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (specialization) => _openForm(initial: specialization),
      onDelete: (specialization) => _provider.delete(specialization.id),
      itemLabel: (specialization) => specialization.name,
    );
  }
}
