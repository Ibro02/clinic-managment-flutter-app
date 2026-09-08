import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_api.dart';
import '../../core/auth_session.dart';
import '../../core/contact_rules.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

/// The patient's own profile and settings (review item C7).
///
/// Everything here is scoped to the signed-in user by the server: the profile
/// endpoint takes the identity from the JWT and there is no user id to send, so
/// this screen structurally cannot edit somebody else's account.
///
/// Two rules from the rulebook shape the layout:
///  - §E: an edit form must not make you retype what you did not come to
///    change, so the password lives behind its own action rather than as two
///    always-empty fields under the name.
///  - §E again: validation messages render *below* the control, which is what
///    `TextFormField`'s `errorText` does and a dialog does not.
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  final _formKey = GlobalKey<FormState>();
  final _authApi = AuthApi();

  late final TextEditingController _firstName;
  late final TextEditingController _lastName;
  late final TextEditingController _phoneNumber;
  late bool _emailRemindersEnabled;
  late String _preferredLanguage;

  bool _isSaving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    // Seeded from the session, which already holds what the server last
    // confirmed - so the form opens filled in rather than blank-then-populated.
    final session = context.read<AuthSession>();
    _firstName = TextEditingController(text: session.firstName ?? '');
    _lastName = TextEditingController(text: session.lastName ?? '');
    _phoneNumber = TextEditingController(text: session.phoneNumber ?? '');
    _emailRemindersEnabled = session.emailRemindersEnabled;
    _preferredLanguage = session.preferredLanguage;
  }

  @override
  void dispose() {
    _firstName.dispose();
    _lastName.dispose();
    _phoneNumber.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    final session = context.read<AuthSession>();
    final token = session.token;
    if (token == null) return; // signed out mid-edit; main.dart is already redirecting

    setState(() {
      _isSaving = true;
      _error = null;
    });
    try {
      final profile = await _authApi.updateProfile(
        token: token,
        firstName: _firstName.text,
        lastName: _lastName.text,
        phoneNumber: _phoneNumber.text,
        emailRemindersEnabled: _emailRemindersEnabled,
        preferredLanguage: _preferredLanguage,
      );
      if (!mounted) return;
      // Apply what the server returned, not what was typed - trailing spaces
      // and an emptied phone come back normalised, and the shell's greeting
      // reads from the same session.
      profile.applyTo(session);
      _firstName.text = profile.firstName;
      _lastName.text = profile.lastName;
      _phoneNumber.text = profile.phoneNumber ?? '';
      setState(() {
        _emailRemindersEnabled = profile.emailRemindersEnabled;
        _preferredLanguage = profile.preferredLanguage;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Vaši podaci su sačuvani.')),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Profil nije sačuvan. ${failureCause(e)}');
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  Future<void> _openChangePassword() async {
    final changed = await showAppDialog<bool>(
      context: context,
      builder: (_) => const _ChangePasswordDialog(),
    );
    if (changed == true && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Lozinka je promijenjena. Koristite je pri sljedećoj prijavi.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<AuthSession>();

    return Scaffold(
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(AppSpacing.md),
          children: [
            AppCard(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Lični podaci', style: context.text.titleMedium),
                  const SizedBox(height: AppSpacing.md),
                  TextFormField(
                    controller: _firstName,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(labelText: 'Ime *'),
                    validator: (value) => ContactRules.text(
                      value,
                      maxLength: ContactRules.maxNameLength,
                      label: 'Ime',
                      required: true,
                    ),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  TextFormField(
                    controller: _lastName,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(labelText: 'Prezime *'),
                    validator: (value) => ContactRules.text(
                      value,
                      maxLength: ContactRules.maxNameLength,
                      label: 'Prezime',
                      required: true,
                    ),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  TextFormField(
                    controller: _phoneNumber,
                    keyboardType: TextInputType.phone,
                    decoration: const InputDecoration(
                      labelText: 'Broj telefona',
                      helperText: 'Npr. +38761123456. Ostavite prazno ako ne želite da vas zovemo.',
                    ),
                    validator: ContactRules.phone,
                  ),
                  const SizedBox(height: AppSpacing.md),
                  // Read-only rather than absent: people look here to check
                  // which address the clinic writes to. Changing it is an
                  // account operation, not a profile edit - the helper says so
                  // instead of leaving a disabled field unexplained.
                  TextFormField(
                    initialValue: session.email ?? '',
                    enabled: false,
                    decoration: const InputDecoration(
                      labelText: 'Email',
                      helperText: 'Email adresa je vaše korisničko ime. Za promjenu se obratite klinici.',
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            AppCard(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Obavijesti i jezik', style: context.text.titleMedium),
                  const SizedBox(height: AppSpacing.xs),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    value: _emailRemindersEnabled,
                    onChanged: (value) => setState(() => _emailRemindersEnabled = value),
                    title: const Text('Podsjetnik na termin putem emaila'),
                    subtitle: const Text(
                      'Šaljemo ga dan prije termina. Obavijesti u aplikaciji dobijate '
                      'i kada je ovo isključeno.',
                    ),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  // Review item 7: this must actually change something, not
                  // just persist unread - the server renders every
                  // notification/email this account receives afterwards
                  // (confirmation, cancellation, reminder, payment, ...) in
                  // whichever language is selected here.
                  DropdownButtonFormField<String>(
                    initialValue: _preferredLanguage,
                    decoration: const InputDecoration(
                      labelText: 'Jezik aplikacije',
                      helperText: 'Obavijesti i email poruke ćete primati na odabranom jeziku.',
                    ),
                    items: const [
                      DropdownMenuItem(value: 'bs', child: Text('Bosanski')),
                      DropdownMenuItem(value: 'en', child: Text('English')),
                    ],
                    onChanged: (value) {
                      if (value != null) setState(() => _preferredLanguage = value);
                    },
                  ),
                ],
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.md),
              AppNotice(tone: AppTone.danger, icon: Icons.error_outline_rounded, message: _error!),
            ],
            const SizedBox(height: AppSpacing.md),
            FilledButton.icon(
              onPressed: _isSaving ? null : _save,
              icon: _isSaving
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.save_outlined),
              label: const Text('Sačuvaj promjene'),
            ),
            const SizedBox(height: AppSpacing.lg),
            AppCard(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Sigurnost', style: context.text.titleMedium),
                  const SizedBox(height: AppSpacing.xs),
                  Text(
                    'Promjena lozinke traži vašu trenutnu lozinku.',
                    style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  OutlinedButton.icon(
                    onPressed: _openChangePassword,
                    icon: const Icon(Icons.lock_outline_rounded, size: 18),
                    label: const Text('Promijeni lozinku'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Password change, behind its own action so the profile form never asks for a
/// password somebody came to leave alone (rulebook §E).
class _ChangePasswordDialog extends StatefulWidget {
  const _ChangePasswordDialog();

  @override
  State<_ChangePasswordDialog> createState() => _ChangePasswordDialogState();
}

class _ChangePasswordDialogState extends State<_ChangePasswordDialog> {
  final _formKey = GlobalKey<FormState>();
  final _authApi = AuthApi();
  final _current = TextEditingController();
  final _next = TextEditingController();
  final _confirm = TextEditingController();

  bool _isSaving = false;
  String? _error;

  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
    _confirm.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    final token = context.read<AuthSession>().token;
    if (token == null) return;

    setState(() {
      _isSaving = true;
      _error = null;
    });
    try {
      final refreshed = await _authApi.changePassword(
        token: token,
        currentPassword: _current.text,
        newPassword: _next.text,
        confirmNewPassword: _confirm.text,
      );

      // The change invalidated every token issued before it, this one included,
      // so the replacement has to be stored or the next call 401s and drops the
      // user at the login screen right after a successful password change.
      if (!mounted) return;
      refreshed.applyTo(context.read<AuthSession>());

      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      // The server names the field it rejected ("currentPassword"), and the
      // message it sends is more specific than anything this screen could
      // invent - show that, don't flatten it to "greška".
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) setState(() => _error = 'Lozinka nije promijenjena. ${failureCause(e)}');
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppDialog(
      title: 'Promjena lozinke',
      subtitle: 'Nakon promjene prijavljujete se novom lozinkom.',
      icon: Icons.lock_outline_rounded,
      width: 460,
      actions: [
        OutlinedButton(
          onPressed: _isSaving ? null : () => Navigator.of(context).pop(false),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: _isSaving ? null : _submit,
          child: _isSaving
              ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Promijeni lozinku'),
        ),
      ],
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextFormField(
              controller: _current,
              obscureText: true,
              decoration: const InputDecoration(labelText: 'Trenutna lozinka *'),
              validator: (value) =>
                  (value == null || value.isEmpty) ? 'Unesite trenutnu lozinku.' : null,
            ),
            const SizedBox(height: AppSpacing.md),
            TextFormField(
              controller: _next,
              obscureText: true,
              decoration: InputDecoration(
                labelText: 'Nova lozinka *',
                helperText: 'Najmanje ${ContactRules.minPasswordLength} karaktera.',
              ),
              validator: ContactRules.password,
            ),
            const SizedBox(height: AppSpacing.md),
            TextFormField(
              controller: _confirm,
              obscureText: true,
              decoration: const InputDecoration(labelText: 'Potvrda nove lozinke *'),
              validator: (value) =>
                  value == _next.text ? null : 'Potvrda se ne poklapa sa novom lozinkom.',
            ),
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.md),
              AppNotice(tone: AppTone.danger, icon: Icons.error_outline_rounded, message: _error!),
            ],
          ],
        ),
      ),
    );
  }
}
