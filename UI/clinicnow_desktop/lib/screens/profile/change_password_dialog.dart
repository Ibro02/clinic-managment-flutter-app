import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/auth_api.dart';
import '../../core/auth_session.dart';
import '../../core/contact_rules.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';

/// Changing your own password, which means proving you know the current one
/// (rulebook §E). An administrator resetting somebody else's password is a
/// different flow and does not come through here.
///
/// Its own dialog rather than three more fields on the profile form, for the
/// same reason the mobile app puts it behind its own action: an edit form must
/// not make you retype what you did not come to change.
class ChangePasswordDialog extends StatefulWidget {
  const ChangePasswordDialog({super.key});

  static Future<void> show(BuildContext context) =>
      showDialog<void>(context: context, builder: (_) => const ChangePasswordDialog());

  @override
  State<ChangePasswordDialog> createState() => _ChangePasswordDialogState();
}

class _ChangePasswordDialogState extends State<ChangePasswordDialog> {
  final _formKey = GlobalKey<FormState>();
  final _authApi = AuthApi();

  final _currentPassword = TextEditingController();
  final _newPassword = TextEditingController();
  final _confirmPassword = TextEditingController();

  bool _isSaving = false;
  String? _error;

  @override
  void dispose() {
    _currentPassword.dispose();
    _newPassword.dispose();
    _confirmPassword.dispose();
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
        currentPassword: _currentPassword.text,
        newPassword: _newPassword.text,
        confirmNewPassword: _confirmPassword.text,
      );
      if (!mounted) return;

      // The change invalidated every token issued before it, this one included,
      // so the replacement has to be stored or the next call 401s and drops the
      // user at the login screen right after a successful password change.
      refreshed.applyTo(context.read<AuthSession>());

      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Lozinka je promijenjena.')),
      );
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = failureCause(error);
        _isSaving = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppDialog(
      title: 'Promjena lozinke',
      subtitle: 'Nova lozinka mora imati najmanje ${ContactRules.minPasswordLength} karaktera.',
      icon: Icons.lock_outline,
      width: 460,
      actions: [
        OutlinedButton(
          onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: _isSaving ? null : _submit,
          child: _isSaving
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Sačuvaj'),
        ),
      ],
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            AppFormSection(
              children: [
                AppField(
                  label: 'Trenutna lozinka',
                  required: true,
                  child: TextFormField(
                    controller: _currentPassword,
                    obscureText: true,
                    validator: (value) => (value == null || value.isEmpty)
                        ? 'Unesite trenutnu lozinku.'
                        : null,
                  ),
                ),
                AppField(
                  label: 'Nova lozinka',
                  required: true,
                  child: TextFormField(
                    controller: _newPassword,
                    obscureText: true,
                    validator: (value) => (value == null || value.length < ContactRules.minPasswordLength)
                        ? 'Lozinka mora imati najmanje ${ContactRules.minPasswordLength} karaktera.'
                        : null,
                  ),
                ),
                AppField(
                  label: 'Potvrda nove lozinke',
                  required: true,
                  child: TextFormField(
                    controller: _confirmPassword,
                    obscureText: true,
                    validator: (value) =>
                        value != _newPassword.text ? 'Lozinke se ne podudaraju.' : null,
                  ),
                ),
              ],
            ),
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.md),
              AppNotice(tone: AppTone.danger, message: _error!),
            ],
          ],
        ),
      ),
    );
  }
}
