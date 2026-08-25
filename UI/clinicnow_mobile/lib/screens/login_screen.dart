import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../core/api_exception.dart';
import '../core/auth_api.dart';
import '../core/auth_session.dart';
import 'register_screen.dart';

/// Only patients belong on the mobile app - a staff/doctor/admin account
/// logging in here is rejected client-side with a clear message (the
/// backend's own per-endpoint authorization is the real enforcement; this is
/// just the right UX - rulebook Part II §K role-aware navigation).
const List<String> _kAllowedMobileRoles = ['Patient'];

/// Patient login screen. `main.dart` watches `AuthSession.isLoggedIn` and
/// swaps to `AppShell` automatically once `_submit` populates the session.
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

      final isAllowed = result.roles.any(_kAllowedMobileRoles.contains);
      if (!isAllowed) {
        await _authApi.logout(result.accessToken);
        setState(() => _errorMessage =
            'Ovaj nalog nema pristup mobilnoj aplikaciji. Osoblje se prijavljuje kroz desktop aplikaciju.');
        return;
      }

      if (!mounted) return;
      result.applyTo(context.read<AuthSession>());
    } on ApiException catch (e) {
      setState(() => _errorMessage = e.message);
    } catch (_) {
      setState(() =>
          _errorMessage = 'Neočekivana greška. Provjerite internetsku vezu.');
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: FormBuilder(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.local_hospital, size: 56),
                  const SizedBox(height: 12),
                  Text(
                    'ClinicNow',
                    style: Theme.of(context).textTheme.headlineMedium,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 32),
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
                      style:
                          TextStyle(color: Theme.of(context).colorScheme.error),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _isSubmitting ? null : _submit,
                    child: _isSubmitting
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Prijava'),
                  ),
                  const SizedBox(height: 12),
                  TextButton(
                    onPressed: _isSubmitting
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                  builder: (_) => const RegisterScreen()),
                            ),
                    child: const Text('Nemate nalog? Registrujte se'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
