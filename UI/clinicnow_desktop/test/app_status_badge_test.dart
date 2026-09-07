import 'package:clinicnow_desktop/core/app_theme.dart';
import 'package:clinicnow_desktop/core/design_tokens.dart';
import 'package:clinicnow_desktop/widgets/ui/app_badge.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The status pill is rendered inside `AppDataTable` rows of a fixed
/// `AppSizes.tableRowHeight`. Its label used to be an unconstrained `Text`, so
/// a long status in a narrow column wrapped to a second line and pushed the
/// badge past its row - the "BOTTOM OVERFLOWED BY 20 PIXELS" stripes on the
/// Termini screen.
///
/// A render overflow raises a `FlutterError`, which fails a widget test on its
/// own: these tests need no explicit assertion about the overflow, only a
/// realistic cell to render into. The `expect`s below are about the label
/// surviving, not about the bug.
void main() {
  Future<void> pumpInCell(WidgetTester tester, String label, {double width = 120}) {
    return tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light(),
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: width,
              height: AppSizes.tableRowHeight,
              child: Align(
                alignment: Alignment.centerLeft,
                child: AppStatusBadge(label: label, tone: AppTone.danger),
              ),
            ),
          ),
        ),
      ),
    );
  }

  testWidgets('a long status fits a table cell without overflowing', (tester) async {
    // The real label from the Termini payment column that overflowed.
    await pumpInCell(tester, 'Povrat nije uspio');

    expect(find.text('Povrat nije uspio'), findsOneWidget);
  });

  testWidgets('an absurdly long status still fits', (tester) async {
    await pumpInCell(tester, 'Otkazan zbog nedostupnosti doktora i vraćenog iznosa', width: 90);

    // Ellipsised rather than wrapped - one line is what keeps it inside the row.
    final text = tester.widget<Text>(find.byType(Text).last);
    expect(text.maxLines, 1);
    expect(text.overflow, TextOverflow.ellipsis);
  });

  testWidgets('a short status is unaffected', (tester) async {
    await pumpInCell(tester, 'Potvrđen', width: 300);

    expect(find.text('Potvrđen'), findsOneWidget);
  });
}
