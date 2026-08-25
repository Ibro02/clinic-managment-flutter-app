import 'package:flutter_test/flutter_test.dart';

import 'package:clinicnow_desktop/main.dart';

void main() {
  testWidgets('App boots to the login screen with a working form',
      (WidgetTester tester) async {
    await tester.pumpWidget(const ClinicNowDesktopApp());
    await tester.pumpAndSettle();

    // The login screen (the app's initial route) renders its title and both
    // credential fields.
    expect(find.text('ClinicNow — Osoblje'), findsOneWidget);
    expect(find.text('Email'), findsOneWidget);
    expect(find.text('Lozinka'), findsOneWidget);

    // Submitting an empty form triggers client-side validation instead of
    // calling the API (rulebook §4: full validation incl. required fields).
    await tester.tap(find.text('Prijava'));
    await tester.pumpAndSettle();

    expect(find.text('Email je obavezan.'), findsOneWidget);
    expect(find.text('Lozinka je obavezna.'), findsOneWidget);
  });
}
