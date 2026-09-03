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
import '../../core/design_tokens.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
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
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novi doktor' : 'Uredi doktora',
          subtitle: initial == null
              ? 'Kreira se i korisnički nalog za prijavu doktora.'
              : 'Izmjena profila doktora.',
          icon: Icons.badge_outlined,
          width: 640,
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
                      if (selectedLocationId == null) {
                        setDialogState(
                          () => fieldErrors = {
                            'locationId': ['Klinika je obavezna.'],
                          },
                        );
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
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Credentials exist only on create - rulebook §E: an edit form
                // never forces the user to re-enter a password.
                if (initial == null) ...[
                  AppFormSection(
                    label: 'Pristupni podaci',
                    children: [
                      AppField(
                        label: 'Email',
                        required: true,
                        child: FormBuilderTextField(
                          name: 'email',
                          decoration: InputDecoration(
                            hintText: 'ime@domena.com',
                            errorText: fieldErrors['email']?.first,
                          ),
                          keyboardType: TextInputType.emailAddress,
                          validator: FormBuilderValidators.compose([
                            FormBuilderValidators.required(errorText: 'Email je obavezan.'),
                            FormBuilderValidators.email(errorText: 'Unesite ispravnu email adresu.'),
                          ]),
                        ),
                      ),
                      AppField(
                        label: 'Početna lozinka',
                        required: true,
                        help: 'Najmanje 8 karaktera. Doktor je može promijeniti nakon prijave.',
                        child: FormBuilderTextField(
                          name: 'password',
                          decoration: InputDecoration(errorText: fieldErrors['password']?.first),
                          obscureText: true,
                          validator: FormBuilderValidators.compose([
                            FormBuilderValidators.required(errorText: 'Lozinka je obavezna.'),
                            FormBuilderValidators.minLength(8, errorText: 'Najmanje 8 karaktera.'),
                          ]),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: AppSpacing.lg),
                ],
                AppFormSection(
                  label: 'Profil',
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
                          label: 'Broj telefona',
                          help: 'Opcionalno.',
                          child: FormBuilderTextField(
                            name: 'phoneNumber',
                            decoration: const InputDecoration(hintText: '+387 …'),
                            keyboardType: TextInputType.phone,
                          ),
                        ),
                        AppField(
                          label: 'Broj licence',
                          help: 'Opcionalno.',
                          child: FormBuilderTextField(name: 'licenseNumber'),
                        ),
                      ],
                    ),
                    // A doctor practices at exactly one clinic (1:1) - chosen
                    // here, once, when the profile is created/edited, never
                    // re-chosen per appointment (that's where this used to live,
                    // letting staff pick a doctor and an unrelated clinic).
                    AppField(
                      label: 'Klinika',
                      required: true,
                      child: DropdownButtonFormField<int>(
                        initialValue: selectedLocationId,
                        decoration: InputDecoration(
                          hintText: 'Odaberite kliniku',
                          errorText: fieldErrors['locationId']?.first,
                        ),
                        items: locations
                            .map(
                              (l) => DropdownMenuItem(value: l.id, child: Text('${l.name} (${l.cityName})')),
                            )
                            .toList(),
                        onChanged: (value) => setDialogState(() => selectedLocationId = value),
                      ),
                    ),
                    AppField(
                      label: 'Biografija',
                      help: 'Opcionalno. Prikazuje se pacijentu pri odabiru doktora.',
                      child: FormBuilderTextField(
                        name: 'bio',
                        decoration: const InputDecoration(hintText: 'Kratka biografija'),
                        maxLines: 3,
                      ),
                    ),
                    AppField(
                      label: 'Specijalizacije',
                      required: true,
                      help: fieldErrors['specializationIds']?.first,
                      child: Wrap(
                        spacing: AppSpacing.xs,
                        runSpacing: AppSpacing.xs,
                        children: specializations
                            .map(
                              (s) => FilterChip(
                                label: Text(s.name),
                                selected: selectedSpecializationIds.contains(s.id),
                                onSelected: (selected) => setDialogState(() {
                                  if (selected) {
                                    selectedSpecializationIds.add(s.id);
                                  } else {
                                    selectedSpecializationIds.remove(s.id);
                                  }
                                }),
                              ),
                            )
                            .toList(),
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

    return PagedCodebookTable<Doctor>(
      key: _tableKey,
      title: 'Doktori',
      searchHint: 'Pretraga po imenu i prezimenu',
      provider: _doctorProvider,
      orderBy: 'LastName',
      canWrite: canWrite,
      buildColumns: () => [
        // Rulebook §K wants an image beside the name; doctors have no photo in
        // the model, so an initials avatar carries the same scanning benefit.
        AppColumn(
          label: 'Ime i prezime',
          sortKey: 'LastName',
          flex: 2,
          cell: (context, doctor) => Row(
            children: [
              AppAvatar(name: doctor.fullName, size: 30),
              const SizedBox(width: AppSpacing.xs),
              Expanded(child: Text(doctor.fullName, overflow: TextOverflow.ellipsis)),
            ],
          ),
        ),
        AppColumn(
          label: 'Email',
          flex: 2,
          cell: (context, doctor) => Text(doctor.email, overflow: TextOverflow.ellipsis),
        ),
        AppColumn(
          label: 'Klinika',
          cell: (context, doctor) => Text(doctor.locationName, overflow: TextOverflow.ellipsis),
        ),
        AppColumn(
          label: 'Specijalizacije',
          flex: 2,
          cell: (context, doctor) => Text(
            doctor.specializations.isEmpty ? '—' : doctor.specializations.join(', '),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
      extraRowActions: (doctor) => [
        AppRowAction(
          icon: Icons.event_note_outlined,
          tooltip: 'Raspored i blokade',
          onPressed: () => Navigator.of(
            context,
          ).push(MaterialPageRoute(builder: (_) => DoctorScheduleScreen(doctor: doctor))),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (doctor) => _openForm(initial: doctor),
      onDelete: (doctor) => _doctorProvider.delete(doctor.id),
      itemLabel: (doctor) => doctor.fullName,
      addLabel: 'Dodaj doktora',
    );
  }
}
