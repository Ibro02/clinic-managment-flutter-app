import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/medical_service.dart';
import '../../providers/medical_service_provider.dart';
import '../../widgets/paged_codebook_table.dart';

class MedicalServiceScreen extends StatefulWidget {
  const MedicalServiceScreen({super.key});

  @override
  State<MedicalServiceScreen> createState() => _MedicalServiceScreenState();
}

class _MedicalServiceScreenState extends State<MedicalServiceScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<MedicalService>>();
  late final MedicalServiceProvider _provider;

  @override
  void initState() {
    super.initState();
    _provider = MedicalServiceProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({MedicalService? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Nova usluga' : 'Uredi uslugu'),
          content: SizedBox(
            width: 420,
            child: FormBuilder(
              key: formKey,
              initialValue: {
                'name': initial?.name ?? '',
                'description': initial?.description ?? '',
                'price': initial?.price.toStringAsFixed(2) ?? '',
                'durationMinutes': initial?.durationMinutes.toString() ?? '',
              },
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  FormBuilderTextField(
                    name: 'name',
                    decoration: InputDecoration(
                      labelText: 'Naziv',
                      errorText: fieldErrors['name']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Naziv je obavezan.'),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'description',
                    decoration: const InputDecoration(labelText: 'Opis (opcionalno)'),
                    maxLines: 3,
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: FormBuilderTextField(
                          name: 'price',
                          decoration: InputDecoration(
                            labelText: 'Cijena (KM)',
                            errorText: fieldErrors['price']?.first,
                          ),
                          keyboardType: const TextInputType.numberWithOptions(decimal: true),
                          validator: FormBuilderValidators.compose([
                            FormBuilderValidators.required(errorText: 'Cijena je obavezna.'),
                            FormBuilderValidators.numeric(errorText: 'Unesite ispravan broj.'),
                            FormBuilderValidators.min(0, errorText: 'Cijena ne može biti negativna.'),
                          ]),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: FormBuilderTextField(
                          name: 'durationMinutes',
                          decoration: InputDecoration(
                            labelText: 'Trajanje (min)',
                            errorText: fieldErrors['durationMinutes']?.first,
                          ),
                          keyboardType: TextInputType.number,
                          validator: FormBuilderValidators.compose([
                            FormBuilderValidators.required(errorText: 'Trajanje je obavezno.'),
                            FormBuilderValidators.integer(errorText: 'Unesite cijeli broj.'),
                            FormBuilderValidators.min(1, errorText: 'Trajanje mora biti veće od 0.'),
                          ]),
                        ),
                      ),
                    ],
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
                      final description = (form.value['description'] as String?)?.trim();
                      final request = {
                        'name': form.value['name'],
                        'description': description?.isEmpty == true ? null : description,
                        'price': double.parse(form.value['price'] as String),
                        'durationMinutes': int.parse(form.value['durationMinutes'] as String),
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
    return PagedCodebookTable<MedicalService>(
      key: _tableKey,
      title: 'Usluge',
      searchHint: 'Pretraga po nazivu',
      provider: _provider,
      buildColumns: () => const [
        DataColumn(label: Text('Naziv')),
        DataColumn(label: Text('Cijena')),
        DataColumn(label: Text('Trajanje')),
      ],
      buildCells: (service) => [
        DataCell(Text(service.name)),
        DataCell(Text('${service.price.toStringAsFixed(2)} KM')),
        DataCell(Text('${service.durationMinutes} min')),
      ],
      onAdd: () => _openForm(),
      onEdit: (service) => _openForm(initial: service),
      onDelete: (service) => _provider.delete(service.id),
      itemLabel: (service) => service.name,
    );
  }
}
