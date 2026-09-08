import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_api.dart';
import '../../core/auth_session.dart';
import '../../core/contact_rules.dart';
import '../../core/design_tokens.dart';
import '../../models/staff_member.dart';
import '../../providers/staff_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

/// Administrator-only staff (front-desk/administrative) account management -
/// the gap review item 1 named directly: Staff accounts had no screen, no
/// endpoint, and no way to create one short of inserting into the database.
///
/// Unlike [DoctorScreen]/[PatientScreen], email is editable here even after
/// creation (via `AuthApi.updateEmail`, review item 2 - "Administrator treba
/// imati SVE privilegije") since a Staff account has no self-service profile
/// screen of its own to fix a typo from.
class StaffScreen extends StatefulWidget {
  const StaffScreen({super.key});

  @override
  State<StaffScreen> createState() => _StaffScreenState();
}

class _StaffScreenState extends State<StaffScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<StaffMember>>();
  final _authApi = AuthApi();
  late final StaffProvider _staffProvider;

  @override
  void initState() {
    super.initState();
    _staffProvider = StaffProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({StaffMember? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};
    final token = context.read<AuthSession>().token;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novi član osoblja' : 'Uredi člana osoblja',
          subtitle: initial == null
              ? 'Kreira se korisnički nalog za prijavu.'
              : 'Izmjena naloga osoblja.',
          icon: Icons.badge_outlined,
          width: 560,
          actions: [
            OutlinedButton(
              onPressed: isSubmitting ? null : () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: isSubmitting || token == null
                  ? null
                  : () async {
                      final form = formKey.currentState;
                      if (form == null || !form.saveAndValidate()) return;

                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });

                      final phone = (form.value['phoneNumber'] as String?)?.trim();
                      final email = (form.value['email'] as String).trim();

                      try {
                        if (initial == null) {
                          await _staffProvider.insert({
                            'email': email,
                            'password': form.value['password'],
                            'firstName': form.value['firstName'],
                            'lastName': form.value['lastName'],
                            'phoneNumber': phone?.isEmpty == true ? null : phone,
                          });
                        } else {
                          // Two calls because the backend deliberately keeps
                          // "change email" (admin-only, any account) separate
                          // from the ordinary profile update every role uses
                          // for its own name/phone - see AuthController.
                          if (email != initial.email) {
                            await _authApi.updateEmail(token: token, userId: initial.id, email: email);
                          }
                          await _staffProvider.update(initial.id, {
                            'firstName': form.value['firstName'],
                            'lastName': form.value['lastName'],
                            'phoneNumber': phone?.isEmpty == true ? null : phone,
                          });
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
              'email': initial?.email ?? '',
              'firstName': initial?.firstName ?? '',
              'lastName': initial?.lastName ?? '',
              'phoneNumber': initial?.phoneNumber ?? '',
            },
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
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
                        validator: (value) => ContactRules.email(value, required: true),
                      ),
                    ),
                    // Credentials exist only on create - rulebook §E: an edit
                    // form never forces the user to re-enter a password.
                    if (initial == null)
                      AppField(
                        label: 'Početna lozinka',
                        required: true,
                        help: 'Najmanje 8 karaktera. Osoblje je može promijeniti nakon prijave.',
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
                    AppField(
                      label: 'Broj telefona',
                      help: 'Opcionalno.',
                      child: FormBuilderTextField(
                        name: 'phoneNumber',
                        decoration: InputDecoration(
                          hintText: '+387 …',
                          errorText: fieldErrors['phoneNumber']?.first,
                        ),
                        keyboardType: TextInputType.phone,
                        validator: ContactRules.phone,
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
    return PagedCodebookTable<StaffMember>(
      key: _tableKey,
      title: 'Osoblje',
      subtitle: 'Nalozi front-desk i administrativnog osoblja.',
      searchHint: 'Pretraga po imenu i prezimenu',
      provider: _staffProvider,
      orderBy: 'LastName',
      buildColumns: () => [
        AppColumn(
          label: 'Ime i prezime',
          sortKey: 'LastName',
          flex: 2,
          cell: (context, staff) => Row(
            children: [
              AppAvatar(name: staff.fullName, size: 30),
              const SizedBox(width: AppSpacing.xs),
              Expanded(child: Text(staff.fullName, overflow: TextOverflow.ellipsis)),
            ],
          ),
        ),
        AppColumn(
          label: 'Email',
          flex: 2,
          cell: (context, staff) => Text(staff.email, overflow: TextOverflow.ellipsis),
        ),
        AppColumn(
          label: 'Telefon',
          cell: (context, staff) => Text(staff.phoneNumber ?? '—', overflow: TextOverflow.ellipsis),
        ),
        AppColumn(
          label: 'Status',
          width: 110,
          cell: (context, staff) => AppStatusBadge(
            label: staff.isActive ? 'Aktivan' : 'Deaktiviran',
            tone: staff.isActive ? AppTone.success : AppTone.neutral,
          ),
        ),
      ],
      onAdd: () => _openForm(),
      onEdit: (staff) => _openForm(initial: staff),
      onDelete: (staff) => _staffProvider.delete(staff.id),
      itemLabel: (staff) => staff.fullName,
      addLabel: 'Dodaj člana osoblja',
    );
  }
}
