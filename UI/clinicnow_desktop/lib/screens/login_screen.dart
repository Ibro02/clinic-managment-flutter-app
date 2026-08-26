import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../core/api_exception.dart';
import '../core/auth_api.dart';
import '../core/auth_session.dart';
import '../core/roles.dart';

/// Roles allowed to sign in on the staff desktop app. A patient account that
/// tries to log in here is rejected client-side with a clear message - the
/// mobile app is where patients belong (rulebook Part II §K role-aware
/// navigation; the backend independently enforces per-endpoint authorization
/// regardless of what this check does, this is just the right UX).
const List<String> _kAllowedDesktopRoles = [
  Roles.administrator,
  Roles.staff,
  Roles.doctor,
];

/// Staff login screen. `main.dart` watches `AuthSession.isLoggedIn` and swaps
/// to `AppShell` automatically once `_submit` populates the session - this
/// screen never navigates directly, which is also what makes an HTTP 401
/// anywhere in the app (session cleared by `BaseProvider`) redirect back here
/// automatically too.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey = GlobalKey<FormBuilderState>();
  final _authApi = AuthApi();

  bool _isSubmitting = false;
  String? _errorMessage;

  Future<void> _submit() async {
    final form = _formKey.currentState;
    if (form == null || !form.saveAndValidate()) return;

    setState(() {
      _isSubmitting = true;
      _errorMessage = null;
    });

    final email = form.value['email'] as String;
    final password = form.value['password'] as String;

    try {
      final result = await _authApi.login(email: email, password: password);

      final isAllowed = result.roles.any(_kAllowedDesktopRoles.contains);
      if (!isAllowed) {
        // Token was already issued server-side; revoke it immediately rather
        // than leaving a valid-but-unused token sitting around.
        await _authApi.logout(result.accessToken);
        setState(
          () => _errorMessage =
              'Ovaj nalog nema pristup desktop aplikaciji. Pacijenti se prijavljuju kroz mobilnu aplikaciju.',
        );
        return;
      }

      if (!mounted) return;
      result.applyTo(context.read<AuthSession>());
    } on ApiException catch (e) {
      setState(() => _errorMessage = e.message);
    } catch (_) {
      setState(
        () =>
            _errorMessage = 'Neočekivana greška. Provjerite internetsku vezu.',
      );
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: FormBuilder(
                  key: _formKey,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      CircleAvatar(
                        radius: 28,
                        backgroundColor: colorScheme.primaryContainer,
                        child: Icon(
                          Icons.local_hospital,
                          color: colorScheme.onPrimaryContainer,
                          size: 28,
                        ),
                      ),
                      const SizedBox(height: 20),
                      Text(
                        'ClinicNow — Osoblje',
                        style: Theme.of(context).textTheme.headlineSmall,
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Prijavite se da pristupite administraciji klinike.',
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 24),
                      FormBuilderTextField(
                        name: 'email',
                        decoration: const InputDecoration(labelText: 'Email'),
                        keyboardType: TextInputType.emailAddress,
                        validator: FormBuilderValidators.compose([
                          FormBuilderValidators.required(
                            errorText: 'Email je obavezan.',
                          ),
                          FormBuilderValidators.email(
                            errorText: 'Unesite ispravnu email adresu.',
                          ),
                        ]),
                        enabled: !_isSubmitting,
                      ),
                      const SizedBox(height: 16),
                      FormBuilderTextField(
                        name: 'password',
                        decoration: const InputDecoration(labelText: 'Lozinka'),
                        obscureText: true,
                        validator: FormBuilderValidators.required(
                          errorText: 'Lozinka je obavezna.',
                        ),
                        enabled: !_isSubmitting,
                        onSubmitted: (_) => _submit(),
                      ),
                      if (_errorMessage != null) ...[
                        const SizedBox(height: 16),
                        Text(
                          _errorMessage!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ],
                      const SizedBox(height: 24),
                      FilledButton(
                        onPressed: _isSubmitting ? null : _submit,
                        child: _isSubmitting
                            ? const SizedBox(
                                height: 20,
                                width: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Text('Prijava'),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
