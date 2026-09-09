import 'package:clinicnow_mobile/core/app_theme.dart';
import 'package:clinicnow_mobile/widgets/home_summary_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _host(Widget child) => MaterialApp(
      theme: AppTheme.light(),
      home: Scaffold(body: child),
    );

void main() {
  testWidgets('greets the patient by name and shows both counters',
      (WidgetTester tester) async {
    await tester.pumpWidget(_host(const HomeSummaryCard(
      name: 'Amina Hodžić',
      pendingCount: 2,
      confirmedCount: 3,
    )));

    expect(find.text('Amina Hodžić'), findsOneWidget);
    expect(find.text(HomeSummaryCard.greetingFor(DateTime.now())), findsOneWidget);
    expect(find.text('Na čekanju'), findsOneWidget);
    expect(find.text('2'), findsOneWidget);
    expect(find.text('Potvrđeni'), findsOneWidget);
    expect(find.text('3'), findsOneWidget);
  });

  testWidgets('shows a placeholder instead of a zero when the counts failed to load',
      (WidgetTester tester) async {
    await tester.pumpWidget(_host(const HomeSummaryCard(name: 'Amina Hodžić')));

    // A wrong "0" would read as "my appointment is gone".
    expect(find.text('0'), findsNothing);
    expect(find.text('—'), findsNWidgets(2));
  });

  testWidgets('falls back to a nameless greeting for a profile with no name',
      (WidgetTester tester) async {
    await tester.pumpWidget(_host(const HomeSummaryCard(name: '   ')));

    expect(find.text('Dobrodošli'), findsOneWidget);
    expect(find.text('u ClinicNow'), findsOneWidget);
  });

  test('the greeting follows the local time of day', () {
    expect(HomeSummaryCard.greetingFor(DateTime(2026, 9, 9, 8)), 'Dobro jutro');
    expect(HomeSummaryCard.greetingFor(DateTime(2026, 9, 9, 14)), 'Dobar dan');
    expect(HomeSummaryCard.greetingFor(DateTime(2026, 9, 9, 21)), 'Dobra večer');
    expect(HomeSummaryCard.greetingFor(DateTime(2026, 9, 9, 2)), 'Dobra večer');
  });
}
