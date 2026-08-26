import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

/// In-app PayPal approval (design doc §5) - never hands off to an external
/// browser. PayPal's `return_url`/`cancel_url` are fixed, non-resolving
/// sentinel URLs (`PaymentService.ReturnUrl`/`CancelUrl` on the backend) -
/// this screen's `NavigationDelegate` intercepts the attempt to navigate to
/// either one and closes with a result *before* the WebView ever tries to
/// actually load them.
class PaymentWebViewScreen extends StatefulWidget {
  final String approveUrl;

  const PaymentWebViewScreen({super.key, required this.approveUrl});

  @override
  State<PaymentWebViewScreen> createState() => _PaymentWebViewScreenState();
}

class _PaymentWebViewScreenState extends State<PaymentWebViewScreen> {
  static const _returnUrlPrefix = 'https://clinicnow.local/payment-return';
  static const _cancelUrlPrefix = 'https://clinicnow.local/payment-cancel';

  late final WebViewController _controller;

  /// PayPal's redirect chain can trigger `onNavigationRequest` more than once
  /// for the same sentinel URL (an intermediate redirect hop, a reload, a
  /// duplicate frame event before the WebView is actually torn down). Without
  /// this guard a second call would run `Navigator.of(context)` against a
  /// context the first `pop()` already deactivated, throwing from inside a
  /// raw navigation callback that nothing else in the app wraps in a
  /// try/catch.
  bool _resultHandled = false;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(NavigationDelegate(
        onNavigationRequest: (request) {
          if (request.url.startsWith(_returnUrlPrefix)) {
            if (_resultHandled) return NavigationDecision.prevent;
            if (!mounted) return NavigationDecision.prevent;
            _resultHandled = true;
            Navigator.of(context).pop(true);
            return NavigationDecision.prevent;
          }
          if (request.url.startsWith(_cancelUrlPrefix)) {
            if (_resultHandled) return NavigationDecision.prevent;
            if (!mounted) return NavigationDecision.prevent;
            _resultHandled = true;
            Navigator.of(context).pop(false);
            return NavigationDecision.prevent;
          }
          return NavigationDecision.navigate;
        },
      ))
      ..loadRequest(Uri.parse(widget.approveUrl));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Plaćanje putem PayPal-a'),
        leading: IconButton(
          icon: const Icon(Icons.close),
          // Same guard as the NavigationDelegate above, and for the same
          // reason: a close tap racing an in-flight sentinel-URL pop would
          // otherwise pop the underlying route a second time.
          onPressed: () {
            if (_resultHandled) return;
            _resultHandled = true;
            Navigator.of(context).pop(false);
          },
        ),
      ),
      body: WebViewWidget(controller: _controller),
    );
  }
}
