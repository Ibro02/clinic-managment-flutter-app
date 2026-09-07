import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/auth_api.dart';
import '../../core/auth_session.dart';
import '../../core/contact_rules.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../core/roles.dart';
import '../../models/doctor.dart';
import '../../providers/doctor_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';
import 'change_password_dialog.dart';

/// The staff-side equivalent of the mobile Profil tab: every role that signs in
/// to the desktop app can correct their own name and phone number here.
///
/// What separates it from the mobile screen is what it *shows without letting
/// you touch it*. A doctor's clinic, specializations and licence number are
/// Administrator/Staff-owned - they describe what the clinic has authorised the
/// doctor to do, so a doctor changing their own specialization would be
/// self-certifying. They are displayed as read-only detail rather than hidden,
/// because a doctor still needs to see what the clinic has on file for them and
/// know who to ask when it is wrong (rulebook §K: an unavailable action is
/// shown disabled with a reason, not silently removed).
///
/// Identity is never sent: `GET`/`PUT api/auth/me` take the user from the JWT,
/// so this dialog structurally cannot edit another account.
class ProfileDialog extends StatefulWidget {
  const ProfileDialog({super.key});

  static Future<void> show(BuildContext context) =>
      showDialog<void>(context: context, builder: (_) => const ProfileDialog());

  @override
  State<ProfileDialog> createState() => _ProfileDialogState();
}

class _ProfileDialogState extends State<ProfileDialog> {
  final _formKey = GlobalKey<FormState>();
  final _authApi = AuthApi();

  final _firstName = TextEditingController();
  final _lastName = TextEditingController();
  final _phoneNumber = TextEditingController();

  bool _isLoading = true;
  bool _isSaving = false;
  String? _loadError;
  String? _saveError;
  String? _savedMessage;

  UserProfile? _profile;

  /// Only populated for a Doctor account - the read-only half of the dialog.
  Doctor? _doctorProfile;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _firstName.dispose();
    _lastName.dispose();
    _phoneNumber.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final session = context.read<AuthSession>();
    final token = session.token;
    if (token == null) return; // signed out; main.dart is already redirecting

    setState(() {
      _isLoading = true;
      _loadError = null;
    });

    try {
      // Read rather than trust the session: the login response never carried
      // the phone number, and emailRemindersEnabled has to be round-tripped
      // (see AuthApi.updateProfile).
      final profile = await _authApi.me(token);

      // A doctor's own profile is a second call because it is a different
      // resource with a different owner - GET api/Doctor/me is read-only to
      // them by design, and only a Doctor account has one at all.
      Doctor? doctorProfile;
      if (session.hasRole(Roles.doctor)) {
        doctorProfile = await DoctorProvider(session).getOwn();
      }

      if (!mounted) return;
      setState(() {
        _profile = profile;
        _doctorProfile = doctorProfile;
        _firstName.text = profile.firstName;
        _lastName.text = profile.lastName;
        _phoneNumber.text = profile.phoneNumber ?? '';
        _isLoading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loadError = failureCause(error);
        _isLoading = false;
      });
    }
  }

  Future<void> _save() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    final session = context.read<AuthSession>();
    final token = session.token;
    final profile = _profile;
    if (token == null || profile == null) return;

    setState(() {
      _isSaving = true;
      _saveError = null;
      _savedMessage = null;
    });

    try {
      final updated = await _authApi.updateProfile(
        token: token,
        firstName: _firstName.text,
        lastName: _lastName.text,
        phoneNumber: _phoneNumber.text,
        emailRemindersEnabled: profile.emailRemindersEnabled,
      );

      if (!mounted) return;
      // Apply what the server returned, not what was typed - trailing spaces
      // and an emptied phone come back normalised, and the top bar's greeting
      // reads from the same session.
      updated.applyTo(session);
      setState(() {
        _profile = updated;
        _firstName.text = updated.firstName;
        _lastName.text = updated.lastName;
        _phoneNumber.text = updated.phoneNumber ?? '';
        _isSaving = false;
        _savedMessage = 'Vaši podaci su sačuvani.';
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _saveError = failureCause(error);
        _isSaving = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<AuthSession>();

    return AppDialog(
      title: 'Moj profil',
      subtitle: session.email ?? '',
      icon: Icons.person_outline,
      width: 620,
      actions: [
        OutlinedButton(
          onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
          child: const Text('Zatvori'),
        ),
        FilledButton(
          onPressed: (_isLoading || _isSaving || _loadError != null) ? null : _save,
          child: _isSaving
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Sačuvaj'),
        ),
      ],
      child: _body(context, session),
    );
  }

  Widget _body(BuildContext context, AuthSession session) {
    if (_isLoading) {
      return const SizedBox(height: 220, child: Center(child: CircularProgressIndicator()));
    }
    if (_loadError != null) {
      return AppErrorState(message: _loadError!, onRetry: _load);
    }

    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppFormSection(
            label: 'Lični podaci',
            children: [
              AppFieldRow(
                children: [
                  AppField(
                    label: 'Ime',
                    required: true,
                    child: TextFormField(
                      controller: _firstName,
                      validator: (value) => ContactRules.text(
                        value,
                        maxLength: ContactRules.maxNameLength,
                        label: 'Ime',
                        required: true,
                      ),
                    ),
                  ),
                  AppField(
                    label: 'Prezime',
                    required: true,
                    child: TextFormField(
                      controller: _lastName,
                      validator: (value) => ContactRules.text(
                        value,
                        maxLength: ContactRules.maxNameLength,
                        label: 'Prezime',
                        required: true,
                      ),
                    ),
                  ),
                ],
              ),
              AppField(
                label: 'Telefon',
                child: TextFormField(
                  controller: _phoneNumber,
                  keyboardType: TextInputType.phone,
                  validator: ContactRules.phone,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          AppFormSection(
            label: 'Nalog',
            children: [
              // Email is the login identity and is mirrored onto a patient's
              // chart, so changing it is an account operation rather than a
              // profile edit - UpdateProfileRequest deliberately has no field
              // for it. Shown, never editable.
              _ReadOnlyField(
                label: 'Email',
                value: _profile!.email,
                note: 'Email je vaša prijava. Mijenja ga administrator klinike.',
              ),
              _RolesField(roles: _profile!.roles),
            ],
          ),
          if (_doctorProfile != null) ...[
            const SizedBox(height: AppSpacing.lg),
            _doctorSection(_doctorProfile!),
          ],
          const SizedBox(height: AppSpacing.lg),
          AppFormSection(
            label: 'Sigurnost',
            children: [
              AppField(
                label: 'Lozinka',
                // Rulebook §E: an edit form must not make you retype what you
                // did not come to change, so the password lives behind its own
                // action rather than as two always-empty fields under the name.
                help: 'Za promjenu lozinke morate unijeti trenutnu lozinku.',
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: OutlinedButton.icon(
                    onPressed: () => ChangePasswordDialog.show(context),
                    icon: const Icon(Icons.lock_outline, size: 16),
                    label: const Text('Promijeni lozinku'),
                  ),
                ),
              ),
            ],
          ),
          if (_savedMessage != null) ...[
            const SizedBox(height: AppSpacing.md),
            AppNotice(tone: AppTone.success, message: _savedMessage!),
          ],
          if (_saveError != null) ...[
            const SizedBox(height: AppSpacing.md),
            AppNotice(tone: AppTone.danger, message: _saveError!),
          ],
        ],
      ),
    );
  }

  /// Everything the clinic decides about a doctor rather than the doctor
  /// themselves. Read-only, with one line saying who to ask - so the field is
  /// informative rather than just inert.
  Widget _doctorSection(Doctor doctor) {
    return AppFormSection(
      label: 'Doktorski profil',
      children: [
        _ReadOnlyField(
          label: 'Klinika',
          value: doctor.locationName,
          note: 'Raspored rada i klinike dodjeljuje administrator klinike.',
        ),
        _SpecializationsField(specializations: doctor.specializations),
        _ReadOnlyField(label: 'Broj licence', value: doctor.licenseNumber ?? '—'),
        _ReadOnlyField(label: 'Biografija', value: doctor.bio ?? '—'),
      ],
    );
  }
}

/// A value the signed-in user may see but not change. Deliberately styled as a
/// field rather than plain text, so the form reads as one thing with some parts
/// locked - not as a form with a paragraph stuck to the bottom.
class _ReadOnlyField extends StatelessWidget {
  final String label;
  final String value;
  final String? note;

  const _ReadOnlyField({required this.label, required this.value, this.note});

  @override
  Widget build(BuildContext context) {
    final c = context.colors;

    return AppField(
      label: label,
      help: note,
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: AppSpacing.xs + 2),
        decoration: BoxDecoration(
          color: c.surfaceMuted,
          borderRadius: AppRadius.all(AppRadius.sm),
          border: Border.all(color: c.border),
        ),
        child: Text(value, style: context.text.bodyMedium?.copyWith(color: c.textMuted)),
      ),
    );
  }
}

class _RolesField extends StatelessWidget {
  final List<String> roles;

  const _RolesField({required this.roles});

  /// Bosnian labels for the role names the backend stores in English - the same
  /// English-identifier/Bosnian-value split the rest of the app uses.
  static const _labels = {
    Roles.administrator: 'Administrator',
    Roles.staff: 'Osoblje',
    Roles.doctor: 'Doktor',
    Roles.patient: 'Pacijent',
  };

  @override
  Widget build(BuildContext context) {
    return AppField(
      label: 'Uloge',
      help: 'Uloge dodjeljuje administrator klinike.',
      child: Wrap(
        spacing: AppSpacing.xs,
        runSpacing: AppSpacing.xs,
        children: [
          for (final role in roles)
            AppStatusBadge(label: _labels[role] ?? role, tone: AppTone.info, showDot: false),
        ],
      ),
    );
  }
}

class _SpecializationsField extends StatelessWidget {
  final List<String> specializations;

  const _SpecializationsField({required this.specializations});

  @override
  Widget build(BuildContext context) {
    return AppField(
      label: 'Specijalizacije',
      help: 'Specijalizacije dodjeljuje administrator klinike.',
      child: specializations.isEmpty
          ? Text('—', style: context.text.bodyMedium?.copyWith(color: context.colors.textMuted))
          : Wrap(
              spacing: AppSpacing.xs,
              runSpacing: AppSpacing.xs,
              children: [
                for (final specialization in specializations)
                  AppStatusBadge(label: specialization, tone: AppTone.primary, showDot: false),
              ],
            ),
    );
  }
}
