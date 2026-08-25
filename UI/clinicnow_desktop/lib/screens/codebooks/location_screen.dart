import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/city.dart';
import '../../models/location.dart';
import '../../providers/city_provider.dart';
import '../../providers/location_provider.dart';
import '../../widgets/paged_codebook_table.dart';

class LocationScreen extends StatefulWidget {
  const LocationScreen({super.key});

  @override
  State<LocationScreen> createState() => _LocationScreenState();
}

class _LocationScreenState extends State<LocationScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Location>>();
  late final LocationProvider _locationProvider;
  late final CityProvider _cityProvider;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _locationProvider = LocationProvider(authSession);
    _cityProvider = CityProvider(authSession);
  }

  Future<List<City>> _loadCities() async {
    // Codebooks are small (never more than a page's worth in this domain), so
    // the max page size covers "every city" without needing a dedicated
    // unbounded endpoint (rulebook Part II §D forbids those anyway).
    final result = await _cityProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
    return result.resultList;
  }

  /// "Add related FK objects via modal without leaving the flow" (rulebook
  /// Part II §K) - lets staff create a missing City inline while filling out
  /// a Location form, instead of cancelling out to the City screen and back.
  Future<City?> _quickAddCity(BuildContext dialogContext) async {
    final formKey = GlobalKey<FormBuilderState>();
    String? error;

    return showDialog<City>(
      context: dialogContext,
      builder: (innerContext) => StatefulBuilder(
        builder: (innerContext, setDialogState) => AlertDialog(
          title: const Text('Novi grad'),
          content: SizedBox(
            width: 320,
            child: FormBuilder(
              key: formKey,
              child: FormBuilderTextField(
                name: 'name',
                decoration: InputDecoration(labelText: 'Naziv', errorText: error),
                validator: FormBuilderValidators.required(errorText: 'Naziv je obavezan.'),
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(innerContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: () async {
                final form = formKey.currentState;
                if (form == null || !form.saveAndValidate()) return;
                try {
                  final created = await _cityProvider.insert({'name': form.value['name']});
                  if (innerContext.mounted) Navigator.of(innerContext).pop(created);
                } on ApiException catch (e) {
                  setDialogState(() => error = e.message);
                }
              },
              child: const Text('Sačuvaj'),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _openForm({Location? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};
    var cities = await _loadCities();
    var selectedCityId = initial?.cityId ?? (cities.isNotEmpty ? cities.first.id : null);

    if (!mounted) return;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Nova lokacija' : 'Uredi lokaciju'),
          content: SizedBox(
            width: 420,
            child: FormBuilder(
              key: formKey,
              initialValue: {
                'name': initial?.name ?? '',
                'address': initial?.address ?? '',
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
                    name: 'address',
                    decoration: InputDecoration(
                      labelText: 'Adresa',
                      errorText: fieldErrors['address']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Adresa je obavezna.'),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: DropdownButtonFormField<int>(
                          initialValue: selectedCityId,
                          decoration: InputDecoration(
                            labelText: 'Grad',
                            errorText: fieldErrors['cityId']?.first,
                          ),
                          items: cities
                              .map((city) => DropdownMenuItem(value: city.id, child: Text(city.name)))
                              .toList(),
                          onChanged: (value) => setDialogState(() => selectedCityId = value),
                        ),
                      ),
                      IconButton(
                        tooltip: 'Novi grad',
                        icon: const Icon(Icons.add_circle_outline),
                        onPressed: () async {
                          final created = await _quickAddCity(dialogContext);
                          if (created == null) return;
                          final refreshed = await _loadCities();
                          setDialogState(() {
                            cities = refreshed;
                            selectedCityId = created.id;
                          });
                        },
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
                      if (form == null || !form.saveAndValidate() || selectedCityId == null) return;
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });
                      final request = {
                        'name': form.value['name'],
                        'address': form.value['address'],
                        'cityId': selectedCityId,
                      };
                      try {
                        if (initial == null) {
                          await _locationProvider.insert(request);
                        } else {
                          await _locationProvider.update(initial.id, request);
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
    return PagedCodebookTable<Location>(
      key: _tableKey,
      title: 'Lokacije',
      searchHint: 'Pretraga po nazivu',
      provider: _locationProvider,
      buildColumns: () => const [
        DataColumn(label: Text('Naziv')),
        DataColumn(label: Text('Adresa')),
        DataColumn(label: Text('Grad')),
      ],
      buildCells: (location) => [
        DataCell(Text(location.name)),
        DataCell(Text(location.address)),
        DataCell(Text(location.cityName)),
      ],
      onAdd: () => _openForm(),
      onEdit: (location) => _openForm(initial: location),
      onDelete: (location) => _locationProvider.delete(location.id),
      itemLabel: (location) => location.name,
    );
  }
}
