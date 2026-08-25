import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/doctor.dart';
import '../../models/location.dart';
import '../../models/specialization.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/location_provider.dart';
import '../../providers/specialization_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import 'doctor_schedule_screen.dart';

class DoctorScreen extends StatefulWidget {
  const DoctorScreen({super.key});

  @override
  State<DoctorScreen> createState() => _DoctorScreenState();
}

class _DoctorScreenState extends State<DoctorScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Doctor>>();
  late final DoctorProvider _doctorProvider;
  late final SpecializationProvider _specializationProvider;
  late final LocationProvider _locationProvider;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _doctorProvider = DoctorProvider(authSession);
    _specializationProvider = SpecializationProvider(authSession);
    _locationProvider = LocationProvider(authSession);
  }

  Future<List<Specialization>> _loadSpecializations() async {
    final result = await _specializationProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
    return result.resultList;
  }

  Future<List<Location>> _loadLocations() async {
    final result = await _locationProvider.getPaged({'pageSize': 100, 'orderBy': 'Name'});
    return result.resultList;
  }

  Future<void> _openForm({Doctor? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};
    final specializations = await _loadSpecializations();
    final locations = await _loadLocations();
    var selectedSpecializationIds = <int>{...(initial?.specializationIds ?? [])};
    var selectedLocationId = initial?.locationId ?? (locations.isNotEmpty ? locations.first.id : null);

    if (!mounted) return;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Novi doktor' : 'Uredi doktora'),
          content: SizedBox(
            width: 460,
            child: SingleChildScrollView(
              child: FormBuilder(
                key: formKey,
                initialValue: {
                  'firstName': initial?.firstName ?? '',
                  'lastName': initial?.lastName ?? '',
                  'phoneNumber': initial?.phoneNumber ?? '',
                  'licenseNumber': initial?.licenseNumber ?? '',
                  'bio': initial?.bio ?? '',
                },
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (initial == null) ...[
                      FormBuilderTextField(
                        name: 'email',
                        decoration: InputDecoration(labelText: 'Email', errorText: fieldErrors['email']?.first),
                        keyboardType: TextInputType.emailAddress,
                        validator: FormBuilderValidators.compose([
                          FormBuilderValidators.required(errorText: 'Email je obavezan.'),
                          FormBuilderValidators.email(errorText: 'Unesite ispravnu email adresu.'),
                        ]),
                      ),
                      const SizedBox(height: 12),
                      FormBuilderTextField(
                        name: 'password',
                        decoration: InputDecoration(
                          labelText: 'Početna lozinka',
                          errorText: fieldErrors['password']?.first,
                          helperText: 'Najmanje 8 karaktera. Doktor je može promijeniti nakon prijave.',
                        ),
                        obscureText: true,
                        validator: FormBuilderValidators.compose([
                          FormBuilderValidators.required(errorText: 'Lozinka je obavezna.'),
                          FormBuilderValidators.minLength(8, errorText: 'Najmanje 8 karaktera.'),
                        ]),
                      ),
                      const SizedBox(height: 12),
                    ],
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
                      name: 'phoneNumber',
                      decoration: const InputDecoration(labelText: 'Broj telefona (opcionalno)'),
                      keyboardType: TextInputType.phone,
                    ),
                    const SizedBox(height: 12),
                    // A doctor practices at exactly one clinic (1:1) - chosen
                    // here, once, when the profile is created/edited, never
                    // re-chosen per appointment (that's where this used to live,
                    // letting staff pick a doctor and an unrelated clinic).
                    DropdownButtonFormField<int>(
                      initialValue: selectedLocationId,
                      decoration: InputDecoration(labelText: 'Klinika', errorText: fieldErrors['locationId']?.first),
                      items: locations.map((l) => DropdownMenuItem(value: l.id, child: Text('${l.name} (${l.cityName})'))).toList(),
                      onChanged: (value) => setDialogState(() => selectedLocationId = value),
                    ),
                    const SizedBox(height: 12),
                    FormBuilderTextField(
                      name: 'licenseNumber',
                      decoration: const InputDecoration(labelText: 'Broj licence (opcionalno)'),
                    ),
                    const SizedBox(height: 12),
                    FormBuilderTextField(
                      name: 'bio',
                      decoration: const InputDecoration(labelText: 'Biografija (opcionalno)'),
                      maxLines: 3,
                    ),
                    const SizedBox(height: 12),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text('Specijalizacije', style: Theme.of(context).textTheme.labelLarge),
                    ),
                    if (fieldErrors['specializationIds'] != null)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: Text(
                          fieldErrors['specializationIds']!.first,
                          style: TextStyle(color: Theme.of(context).colorScheme.error, fontSize: 12),
                        ),
                      ),
                    Wrap(
                      spacing: 8,
                      children: specializations
                          .map((s) => FilterChip(
                                label: Text(s.name),
                                selected: selectedSpecializationIds.contains(s.id),
                                onSelected: (selected) => setDialogState(() {
                                  if (selected) {
                                    selectedSpecializationIds.add(s.id);
                                  } else {
                                    selectedSpecializationIds.remove(s.id);
                                  }
                                }),
                              ))
                          .toList(),
                    ),
                  ],
                ),
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
                      if (selectedLocationId == null) {
                        setDialogState(() => fieldErrors = {'locationId': ['Klinika je obavezna.']});
                        return;
                      }
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });

                      final bio = (form.value['bio'] as String?)?.trim();
                      final license = (form.value['licenseNumber'] as String?)?.trim();
                      final phone = (form.value['phoneNumber'] as String?)?.trim();

                      final request = <String, dynamic>{
                        'firstName': form.value['firstName'],
                        'lastName': form.value['lastName'],
                        'phoneNumber': phone?.isEmpty == true ? null : phone,
                        'locationId': selectedLocationId,
                        'licenseNumber': license?.isEmpty == true ? null : license,
                        'bio': bio?.isEmpty == true ? null : bio,
                        'specializationIds': selectedSpecializationIds.toList(),
                        if (initial == null) 'email': form.value['email'],
                        if (initial == null) 'password': form.value['password'],
                      };

                      try {
                        if (initial == null) {
                          await _doctorProvider.insert(request);
                        } else {
                          await _doctorProvider.update(initial.id, request);
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

    return PagedCodebookTable<Doctor>(
      key: _tableKey,
      title: 'Doktori',
      searchHint: 'Pretraga po imenu i prezimenu',
      provider: _doctorProvider,
      orderBy: 'LastName',
      canWrite: canWrite,
      buildColumns: () => const [
        DataColumn(label: Text('Ime i prezime')),
        DataColumn(label: Text('Email')),
        DataColumn(label: Text('Klinika')),
        DataColumn(label: Text('Specijalizacije')),
      ],
      buildCells: (doctor) => [
        DataCell(Text(doctor.fullName)),
        DataCell(Text(doctor.email)),
        DataCell(Text(doctor.locationName)),
        DataCell(Text(doctor.specializations.isEmpty ? '—' : doctor.specializations.join(', '))),
      ],
      extraRowActions: (doctor) => [
        IconButton(
          tooltip: 'Raspored i blokade',
          icon: const Icon(Icons.event_note_outlined),
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => DoctorScheduleScreen(doctor: doctor)),
          ),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (doctor) => _openForm(initial: doctor),
      onDelete: (doctor) => _doctorProvider.delete(doctor.id),
      itemLabel: (doctor) => doctor.fullName,
    );
  }
}
