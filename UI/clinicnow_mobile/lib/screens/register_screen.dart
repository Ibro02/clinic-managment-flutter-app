import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:provider/provider.dart';

import '../core/api_exception.dart';
import '../core/auth_api.dart';
import '../core/auth_session.dart';

/// Patient self-registration. Always creates a `Patient`-role account - there
/// is no role field on this form at all, matching the backend's
/// `RegisterRequest` (rulebook §5: the server never trusts a client-supplied
/// role, so the client doesn't even offer one).
class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key});

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
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

    try {
      final result = await _authApi.register(
        email: form.value['email'] as String,
        password: form.value['password'] as String,
        firstName: form.value['firstName'] as String,
        lastName: form.value['lastName'] as String,
        phoneNumber: form.value['phoneNumber'] as String?,
      );

      if (!mounted) return;
      result.applyTo(context.read<AuthSession>());

      // The '/' route already reacts to AuthSession and will show AppShell on
      // its own (see main.dart) - popping back to it is all that's needed to
      // reveal that swap, since this screen was pushed on top of it.
      Navigator.of(context).popUntil((route) => route.isFirst);
    } on ApiException catch (e) {
      setState(() {
        _errorMessage = e.message;
        _fieldErrors = e.fieldErrors;
      });
    } catch (_) {
      setState(() =>
          _errorMessage = 'Neočekivana greška. Provjerite internetsku vezu.');
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  Map<String, List<String>> _fieldErrors = const {};

  String? _serverError(String field) =>
      _fieldErrors[field]?.isNotEmpty == true ? _fieldErrors[field]!.first : null;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Registracija')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: FormBuilder(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FormBuilderTextField(
                  name: 'firstName',
                  decoration: InputDecoration(
                    labelText: 'Ime',
                    errorText: _serverError('firstName'),
                  ),
                  validator: FormBuilderValidators.required(
                    errorText: 'Ime je obavezno.',
                  ),
                  enabled: !_isSubmitting,
                ),
                const SizedBox(height: 16),
                FormBuilderTextField(
                  name: 'lastName',
                  decoration: InputDecoration(
                    labelText: 'Prezime',
                    errorText: _serverError('lastName'),
                  ),
                  validator: FormBuilderValidators.required(
                    errorText: 'Prezime je obavezno.',
                  ),
                  enabled: !_isSubmitting,
                ),
                const SizedBox(height: 16),
                FormBuilderTextField(
                  name: 'email',
                  decoration: InputDecoration(
                    labelText: 'Email',
                    errorText: _serverError('email'),
                  ),
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
                  name: 'phoneNumber',
                  decoration: InputDecoration(
                    labelText: 'Broj telefona (opcionalno)',
                    errorText: _serverError('phoneNumber'),
                    hintText: '+38761123456',
                  ),
                  keyboardType: TextInputType.phone,
                  enabled: !_isSubmitting,
                ),
                const SizedBox(height: 16),
                FormBuilderTextField(
                  name: 'password',
                  decoration: InputDecoration(
                    labelText: 'Lozinka',
                    errorText: _serverError('password'),
                    helperText: 'Najmanje 8 karaktera.',
                  ),
                  obscureText: true,
                  validator: FormBuilderValidators.compose([
                    FormBuilderValidators.required(
                      errorText: 'Lozinka je obavezna.',
                    ),
                    FormBuilderValidators.minLength(8,
                        errorText: 'Lozinka mora imati najmanje 8 karaktera.'),
                  ]),
                  enabled: !_isSubmitting,
                ),
                const SizedBox(height: 16),
                FormBuilderTextField(
                  name: 'confirmPassword',
                  decoration:
                      const InputDecoration(labelText: 'Potvrdite lozinku'),
                  obscureText: true,
                  validator: (value) {
                    final password = _formKey.currentState?.fields['password']?.value;
                    if (value != password) return 'Lozinke se ne podudaraju.';
                    return null;
                  },
                  enabled: !_isSubmitting,
                ),
                if (_errorMessage != null) ...[
                  const SizedBox(height: 16),
                  Text(
                    _errorMessage!,
                    style: TextStyle(color: Theme.of(context).colorScheme.error),
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
                      : const Text('Registruj se'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
