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

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(NavigationDelegate(
        onNavigationRequest: (request) {
          if (request.url.startsWith(_returnUrlPrefix)) {
            Navigator.of(context).pop(true);
            return NavigationDecision.prevent;
          }
          if (request.url.startsWith(_cancelUrlPrefix)) {
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
          onPressed: () => Navigator.of(context).pop(false),
        ),
      ),
      body: WebViewWidget(controller: _controller),
    );
  }
}
