import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:clinicnow_mobile/main.dart';

void main() {
  testWidgets('App boots to the login screen with a working form',
      (WidgetTester tester) async {
    await tester.pumpWidget(const ClinicNowMobileApp());
    await tester.pumpAndSettle();

    // The login screen (the app's initial route) renders its title and both
    // credential fields.
    expect(find.text('ClinicNow'), findsOneWidget);
    expect(find.widgetWithText(TextFormField, 'E-mail ili korisničko ime'),
        findsOneWidget);
    expect(find.widgetWithText(TextFormField, 'Lozinka'), findsOneWidget);

    // Submitting an empty form triggers client-side validation instead of
    // calling the API (rulebook §4: full validation incl. required fields).
    await tester.tap(find.text('Prijava'));
    await tester.pumpAndSettle();

    expect(find.text('Ovo polje je obavezno.'), findsOneWidget);
    expect(find.text('Lozinka je obavezna.'), findsOneWidget);
  });
}
