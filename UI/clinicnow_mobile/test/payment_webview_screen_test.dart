import 'package:clinicnow_mobile/screens/payments/payment_webview_screen.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // webview_flutter registers a WebViewPlatform.instance only on Android and
  // iOS. Overriding the target platform to one it does not support reproduces
  // exactly the state a Flutter web / desktop host is in, where constructing a
  // WebViewController used to trip
  //   'A platform implementation for `webview_flutter` has not been set'
  // and take down the whole payment screen.
  testWidgets('falls back to browser approval where no WebView exists', (tester) async {
    // Reset inside the test body: flutter_test asserts every foundation debug
    // variable is unset by the time the body returns, so a tearDown is too late.
    debugDefaultTargetPlatformOverride = TargetPlatform.windows;
    try {
      await tester.pumpWidget(const MaterialApp(
        home: PaymentWebViewScreen(approveUrl: 'https://www.sandbox.paypal.com/checkoutnow?token=ABC123'),
      ));

      // The screen builds at all - previously this threw before first frame.
      expect(tester.takeException(), isNull);

      expect(find.text('PayPal se otvara u pregledniku'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Otvori PayPal'), findsOneWidget);

      // The DNS-error warning must be on screen BEFORE the patient leaves for
      // PayPal. Shown only afterwards, the browser's NXDOMAIN page reads as a
      // failed payment when it actually means the approval went through.
      expect(
        find.textContaining('DNS_PROBE_FINISHED_NXDOMAIN'),
        findsOneWidget,
        reason: 'the sentinel-redirect warning must precede the handoff',
      );

      // The confirm/cancel pair only appears once PayPal has actually been
      // opened - nobody can confirm a payment they never started.
      expect(find.text('Potvrdio/la sam plaćanje'), findsNothing);
    } finally {
      debugDefaultTargetPlatformOverride = null;
    }
  });
}
