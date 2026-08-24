import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../core/api_exception.dart';
import '../core/auth_api.dart';
import '../core/auth_session.dart';
import '../layouts/app_shell.dart';

/// Staff login screen. This UI is already final - only the backend endpoint
/// it calls (`AuthApi.login` -> `POST api/auth/login`) is still pending
/// (Phase 1), so nothing here needs to change once that lands.
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

    final username = form.value['username'] as String;
    final password = form.value['password'] as String;

    try {
      final result =
          await _authApi.login(username: username, password: password);
      if (!mounted) return;

      context.read<AuthSession>().setSession(
            token: result.token,
            username: result.username,
            roles: result.roles,
          );

      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const AppShell()),
      );
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
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: FormBuilder(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'ClinicNow — Osoblje',
                    style: Theme.of(context).textTheme.headlineSmall,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 24),
                  FormBuilderTextField(
                    name: 'username',
                    decoration:
                        const InputDecoration(labelText: 'Korisničko ime'),
                    validator: FormBuilderValidators.required(
                      errorText: 'Korisničko ime je obavezno.',
                    ),
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
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
