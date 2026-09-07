import 'package:flutter/material.dart';

import '../core/auth_api.dart';
import '../core/contact_rules.dart';
import '../core/design_tokens.dart';
import '../core/error_text.dart';
import '../widgets/ui/app_badge.dart';
import '../widgets/ui/app_card.dart';
import '../widgets/ui/app_states.dart';

/// Forgotten-password reset, in the two steps the backend implements
/// (review item C7): ask for a code by email, then redeem it for a new
/// password.
///
/// The first step deliberately gives the same answer whether or not the address
/// has an account - the server will not say, because telling a stranger which
/// emails are registered at a clinic is itself a disclosure. So the copy here
/// says "ako postoji nalog", never "poslali smo vam kod".
class ForgotPasswordScreen extends StatefulWidget {
  /// Prefilled from whatever was already typed on the login screen, so nobody
  /// types their address twice.
  final String initialEmail;

  const ForgotPasswordScreen({super.key, this.initialEmail = ''});

  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

enum _Step { requestCode, enterCode }

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _authApi = AuthApi();

  late final TextEditingController _email = TextEditingController(text: widget.initialEmail);
  final _code = TextEditingController();
  final _newPassword = TextEditingController();
  final _confirmPassword = TextEditingController();

  _Step _step = _Step.requestCode;
  bool _isBusy = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _code.dispose();
    _newPassword.dispose();
    _confirmPassword.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action, {required String failureOutcome}) async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() {
      _isBusy = true;
      _error = null;
    });
    try {
      await action();
    } catch (e) {
      if (mounted) setState(() => _error = '$failureOutcome ${failureCause(e)}');
    } finally {
      if (mounted) setState(() => _isBusy = false);
    }
  }

  Future<void> _requestCode() => _run(
        () async {
          await _authApi.forgotPassword(email: _email.text.trim());
          if (mounted) setState(() => _step = _Step.enterCode);
        },
        failureOutcome: 'Kod nije zatražen.',
      );

  Future<void> _resetPassword() => _run(
        () async {
          await _authApi.resetPassword(
            email: _email.text.trim(),
            code: _code.text.trim(),
            newPassword: _newPassword.text,
            confirmNewPassword: _confirmPassword.text,
          );
          if (!mounted) return;
          // Straight back to login rather than signing them in automatically:
          // the point of the flow is to prove they can now sign in with the
          // password they just chose.
          Navigator.of(context).pop();
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Lozinka je promijenjena. Prijavite se novom lozinkom.')),
          );
        },
        failureOutcome: 'Lozinka nije resetovana.',
      );

  @override
  Widget build(BuildContext context) {
    final isRequestStep = _step == _Step.requestCode;

    return Scaffold(
      appBar: AppBar(title: const Text('Zaboravljena lozinka')),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(AppSpacing.md),
          children: [
            AppCard(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    isRequestStep ? 'Korak 1 od 2' : 'Korak 2 od 2',
                    style: context.text.labelSmall,
                  ),
                  const SizedBox(height: AppSpacing.xs),
                  Text(
                    isRequestStep
                        ? 'Unesite email adresu svog naloga. Poslat ćemo vam kod za '
                              'postavljanje nove lozinke.'
                        : 'Unesite kod iz emaila i novu lozinku.',
                    style: context.text.bodyMedium?.copyWith(color: context.colors.textSecondary),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  TextFormField(
                    controller: _email,
                    enabled: isRequestStep,
                    keyboardType: TextInputType.emailAddress,
                    autocorrect: false,
                    decoration: const InputDecoration(labelText: 'Email *'),
                    validator: (value) => ContactRules.email(value, required: true),
                  ),
                  if (!isRequestStep) ...[
                    const SizedBox(height: AppSpacing.md),
                    TextFormField(
                      controller: _code,
                      textCapitalization: TextCapitalization.characters,
                      autocorrect: false,
                      decoration: const InputDecoration(
                        labelText: 'Kod iz emaila *',
                        helperText: 'Npr. ABCD-EFGH-JKMN. Vrijedi 30 minuta i može se iskoristiti jednom.',
                      ),
                      validator: (value) => (value == null || value.trim().isEmpty)
                          ? 'Unesite kod koji ste dobili na email.'
                          : null,
                    ),
                    const SizedBox(height: AppSpacing.md),
                    TextFormField(
                      controller: _newPassword,
                      obscureText: true,
                      decoration: InputDecoration(
                        labelText: 'Nova lozinka *',
                        helperText: 'Najmanje ${ContactRules.minPasswordLength} karaktera.',
                      ),
                      validator: ContactRules.password,
                    ),
                    const SizedBox(height: AppSpacing.md),
                    TextFormField(
                      controller: _confirmPassword,
                      obscureText: true,
                      decoration: const InputDecoration(labelText: 'Potvrda nove lozinke *'),
                      validator: (value) => value == _newPassword.text
                          ? null
                          : 'Potvrda se ne poklapa sa novom lozinkom.',
                    ),
                  ],
                ],
              ),
            ),
            if (!isRequestStep) ...[
              const SizedBox(height: AppSpacing.md),
              // Not "poslali smo vam kod": the server does not reveal whether
              // this address has an account, so neither can this screen.
              const AppNotice(
                tone: AppTone.info,
                message: 'Ako za ovu email adresu postoji nalog, kod je na putu. '
                    'Provjerite i neželjenu poštu.',
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.md),
              AppNotice(tone: AppTone.danger, icon: Icons.error_outline_rounded, message: _error!),
            ],
            const SizedBox(height: AppSpacing.md),
            FilledButton(
              onPressed: _isBusy ? null : (isRequestStep ? _requestCode : _resetPassword),
              child: _isBusy
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : Text(isRequestStep ? 'Pošalji kod' : 'Postavi novu lozinku'),
            ),
            if (!isRequestStep) ...[
              const SizedBox(height: AppSpacing.xs),
              TextButton(
                onPressed: _isBusy ? null : () => setState(() => _step = _Step.requestCode),
                child: const Text('Nisam dobio kod - pošalji ponovo'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
