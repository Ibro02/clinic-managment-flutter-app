import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
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

/// `webview_flutter` only ships platform implementations for Android
/// (`webview_flutter_android`) and iOS (`webview_flutter_wkwebview`). On any
/// other target - notably Flutter web, which is the only runnable target on a
/// dev machine with no Android emulator - `WebViewPlatform.instance` stays
/// null and merely *constructing* a `WebViewController` trips a framework
/// assertion. Checked up front so an unsupported host degrades to an
/// explanation instead of a red assertion screen.
bool get _webViewSupported =>
    !kIsWeb &&
    (defaultTargetPlatform == TargetPlatform.android ||
        defaultTargetPlatform == TargetPlatform.iOS);

class _PaymentWebViewScreenState extends State<PaymentWebViewScreen> {
  static const _returnUrlPrefix = 'https://clinicnow.local/payment-return';
  static const _cancelUrlPrefix = 'https://clinicnow.local/payment-cancel';

  /// Null on a platform without a WebView implementation - see [_webViewSupported].
  WebViewController? _controller;

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
    if (!_webViewSupported) return;
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
      body: _controller == null
          ? _ExternalBrowserApproval(
              approveUrl: widget.approveUrl,
              onResult: (approved) {
                if (_resultHandled) return;
                _resultHandled = true;
                Navigator.of(context).pop(approved);
              },
            )
          : WebViewWidget(controller: _controller!),
    );
  }
}

/// Fallback approval flow for hosts with no `webview_flutter` implementation
/// (see [_webViewSupported]): hand the approval URL to the system browser -
/// a new tab on Flutter web - and let the patient tell us when they're done.
///
/// Self-reporting is safe because it is not trusted: [onResult] only triggers
/// `POST api/Payment/{id}/capture`, and the backend captures against PayPal
/// itself. An order the buyer never approved fails there and surfaces
/// "Plaćanje nije odobreno na PayPal-u", leaving the payment Pending and
/// retryable - the server stays the sole source of truth about payment.
class _ExternalBrowserApproval extends StatefulWidget {
  final String approveUrl;
  final ValueChanged<bool> onResult;

  const _ExternalBrowserApproval({required this.approveUrl, required this.onResult});

  @override
  State<_ExternalBrowserApproval> createState() => _ExternalBrowserApprovalState();
}

class _ExternalBrowserApprovalState extends State<_ExternalBrowserApproval> {
  bool _opened = false;
  String? _launchError;

  Future<void> _openPayPal() async {
    setState(() => _launchError = null);
    try {
      final launched = await launchUrl(
        Uri.parse(widget.approveUrl),
        mode: LaunchMode.externalApplication,
        webOnlyWindowName: '_blank',
      );
      if (!mounted) return;
      setState(() {
        if (launched) {
          _opened = true;
        } else {
          _launchError = 'Nije moguće otvoriti PayPal stranicu.';
        }
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _launchError = 'Nije moguće otvoriti PayPal stranicu: $e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.open_in_new, size: 48),
            const SizedBox(height: 16),
            Text(
              'PayPal se otvara u pregledniku',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            const Text(
              'Ugrađeni web prikaz je dostupan samo na Android i iOS uređajima, '
              'pa se na ovoj platformi PayPal otvara u novoj kartici preglednika.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 24),
            // The whole sequence is spelled out *before* leaving for PayPal.
            // Step 3 is the one that matters: PayPal's return_url is a
            // deliberately non-resolving sentinel, so a real browser tab ends
            // on a DNS error page. On Android the WebView intercepts that
            // navigation before it loads and the patient never sees it - here
            // they do, and unexplained it reads as a failed payment when it is
            // in fact the signal that the payment succeeded.
            const _ApprovalSteps(),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: _openPayPal,
              icon: const Icon(Icons.open_in_new),
              label: Text(_opened ? 'Ponovo otvori PayPal' : 'Otvori PayPal'),
            ),
            if (_launchError != null) ...[
              const SizedBox(height: 12),
              Text(
                _launchError!,
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ],
            if (_opened) ...[
              const SizedBox(height: 32),
              const Divider(),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () => widget.onResult(true),
                child: const Text('Potvrdio/la sam plaćanje'),
              ),
              const SizedBox(height: 8),
              TextButton(
                onPressed: () => widget.onResult(false),
                child: const Text('Odustani'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// The numbered walkthrough shown before handing off to the browser.
class _ApprovalSteps extends StatelessWidget {
  const _ApprovalSteps();

  static const _steps = [
    'Otvorite PayPal u novoj kartici i odobrite plaćanje.',
    'PayPal će vas potom preusmjeriti na adresu "clinicnow.local", '
        'koja se NEĆE učitati - preglednik će prikazati grešku '
        '(DNS_PROBE_FINISHED_NXDOMAIN).',
    'Ta greška je očekivana i znači da je plaćanje odobreno. '
        'Zatvorite tu karticu.',
    'Vratite se na ovu karticu i pritisnite "Potvrdio/la sam plaćanje".',
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (var i = 0; i < _steps.length; i++)
            Padding(
              padding: EdgeInsets.only(bottom: i == _steps.length - 1 ? 0 : 12),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('${i + 1}.', style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.bold)),
                  const SizedBox(width: 8),
                  Expanded(child: Text(_steps[i], style: theme.textTheme.bodyMedium)),
                ],
              ),
            ),
        ],
      ),
    );
  }
}
