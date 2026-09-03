import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/city.dart';
import '../../providers/city_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

class CityScreen extends StatefulWidget {
  const CityScreen({super.key});

  @override
  State<CityScreen> createState() => _CityScreenState();
}

class _CityScreenState extends State<CityScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<City>>();
  late final CityProvider _provider;

  @override
  void initState() {
    super.initState();
    _provider = CityProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({City? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showAppDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novi grad' : 'Uredi grad',
          subtitle: 'Gradovi se koriste pri unosu lokacija klinika.',
          icon: Icons.location_city_outlined,
          width: 460,
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
                      final request = {'name': form.value['name']};
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
            initialValue: {'name': initial?.name ?? ''},
            child: AppField(
              label: 'Naziv',
              required: true,
              child: FormBuilderTextField(
                name: 'name',
                decoration: InputDecoration(hintText: 'npr. Sarajevo', errorText: fieldErrors['name']?.first),
                validator: FormBuilderValidators.required(errorText: 'Naziv je obavezan.'),
              ),
            ),
          ),
        ),
      ),
    );

    _tableKey.currentState?.load();
  }

  @override
  Widget build(BuildContext context) {
    return PagedCodebookTable<City>(
      key: _tableKey,
      title: 'Gradovi',
      searchHint: 'Pretraga po nazivu',
      provider: _provider,
      buildColumns: () => [
        AppColumn(label: 'Naziv', sortKey: 'Name', cell: (context, city) => Text(city.name)),
      ],
      onAdd: () => _openForm(),
      onEdit: (city) => _openForm(initial: city),
      onDelete: (city) => _provider.delete(city.id),
      itemLabel: (city) => city.name,
    );
  }
}
