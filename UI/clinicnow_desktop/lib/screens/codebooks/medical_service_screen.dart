import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/medical_service.dart';
import '../../models/specialization.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/specialization_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

class MedicalServiceScreen extends StatefulWidget {
  const MedicalServiceScreen({super.key});

  @override
  State<MedicalServiceScreen> createState() => _MedicalServiceScreenState();
}

class _MedicalServiceScreenState extends State<MedicalServiceScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<MedicalService>>();
  late final MedicalServiceProvider _provider;
  late final SpecializationProvider _specializationProvider;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _provider = MedicalServiceProvider(authSession);
    _specializationProvider = SpecializationProvider(authSession);
  }

  /// Same approach as `LocationScreen._loadCities`: the max page size covers
  /// "every specialization" without a dedicated unpaged endpoint.
  Future<List<Specialization>> _loadSpecializations() async {
    final result = await _specializationProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
    return result.resultList;
  }

  Future<void> _openForm({MedicalService? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};
    final specializations = await _loadSpecializations();

    if (!mounted) return;

    // Rulebook §K: don't open a form that cannot be submitted - a service must
    // belong to a specialization, so with none defined the only useful thing to
    // do is say why.
    if (specializations.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Prvo dodajte barem jednu specijalizaciju.')),
      );
      return;
    }

    var selectedSpecializationId = initial?.specializationId ?? specializations.first.id;

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Nova usluga' : 'Uredi uslugu',
          subtitle: 'Cijena i trajanje se koriste pri zakazivanju termina.',
          icon: Icons.local_hospital_outlined,
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
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });
                      final description = (form.value['description'] as String?)?.trim();
                      final request = {
                        'name': form.value['name'],
                        'description': description?.isEmpty == true ? null : description,
                        'specializationId': selectedSpecializationId,
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
          child: FormBuilder(
            key: formKey,
            initialValue: {
              'name': initial?.name ?? '',
              'description': initial?.description ?? '',
              'price': initial?.price.toStringAsFixed(2) ?? '',
              'durationMinutes': initial?.durationMinutes.toString() ?? '',
            },
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Naziv',
                  required: true,
                  child: FormBuilderTextField(
                    name: 'name',
                    decoration: InputDecoration(
                      hintText: 'npr. Pregled dermatologa',
                      errorText: fieldErrors['name']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Naziv je obavezan.'),
                  ),
                ),
                AppField(
                  label: 'Opis',
                  help: 'Opcionalno. Prikazuje se pacijentu pri odabiru usluge.',
                  child: FormBuilderTextField(
                    name: 'description',
                    decoration: const InputDecoration(hintText: 'Kratak opis usluge'),
                    maxLines: 3,
                  ),
                ),
                AppField(
                  label: 'Specijalizacija',
                  required: true,
                  help: 'Uslugu mogu pružati samo doktori sa ovom specijalizacijom.',
                  child: DropdownButtonFormField<int>(
                    initialValue: selectedSpecializationId,
                    decoration: InputDecoration(
                      hintText: 'Odaberite specijalizaciju',
                      errorText: fieldErrors['specializationId']?.first,
                    ),
                    items: specializations
                        .map((s) => DropdownMenuItem(value: s.id, child: Text(s.name)))
                        .toList(),
                    onChanged: (value) => setDialogState(() => selectedSpecializationId = value!),
                  ),
                ),
                AppFieldRow(
                  children: [
                    AppField(
                      label: 'Cijena (KM)',
                      required: true,
                      child: FormBuilderTextField(
                        name: 'price',
                        decoration: InputDecoration(hintText: '0.00', errorText: fieldErrors['price']?.first),
                        keyboardType: const TextInputType.numberWithOptions(decimal: true),
                        validator: FormBuilderValidators.compose([
                          FormBuilderValidators.required(errorText: 'Cijena je obavezna.'),
                          FormBuilderValidators.numeric(errorText: 'Unesite ispravan broj.'),
                          FormBuilderValidators.min(0, errorText: 'Cijena ne može biti negativna.'),
                        ]),
                      ),
                    ),
                    AppField(
                      label: 'Trajanje (min)',
                      required: true,
                      child: FormBuilderTextField(
                        name: 'durationMinutes',
                        decoration: InputDecoration(
                          hintText: '30',
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
      buildColumns: () => [
        AppColumn(label: 'Naziv', sortKey: 'Name', flex: 3, cell: (context, service) => Text(service.name)),
        AppColumn(label: 'Specijalizacija', flex: 2, cell: (context, service) => Text(service.specializationName)),
        // Money and duration are numeric: right-aligned with tabular figures so
        // the decimal points line up down the column.
        AppColumn(
          label: 'Cijena',
          width: 120,
          numeric: true,
          cell: (context, service) => Text('${service.price.toStringAsFixed(2)} KM'),
        ),
        AppColumn(
          label: 'Trajanje',
          width: 110,
          numeric: true,
          cell: (context, service) => Text('${service.durationMinutes} min'),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (service) => _openForm(initial: service),
      onDelete: (service) => _provider.delete(service.id),
      itemLabel: (service) => service.name,
    );
  }
}
