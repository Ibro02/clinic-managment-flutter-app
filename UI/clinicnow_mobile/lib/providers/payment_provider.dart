import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/payment.dart';

class PaymentProvider extends BaseProvider<Payment> {
  PaymentProvider(AuthSession authSession) : super('Payment', authSession);

  @override
  Payment fromJson(Map<String, dynamic> json) => Payment.fromJson(json);

  /// Starts a payment for the given appointment - `insert` already POSTs to
  /// `api/Payment` and decodes the response via `BaseProvider`, no custom
  /// logic needed beyond the request body shape.
  Future<Payment> create(int appointmentId) => insert({'appointmentId': appointmentId});

  Future<Payment> capture(int paymentId) async {
    final response = await http.post(buildUri('api/Payment/$paymentId/capture'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  /// Tells the server the patient closed the PayPal screen without approving,
  /// so this attempt is retired and the next "Plati" isn't refused as a
  /// duplicate (review item C12). Best-effort by design - if it never arrives
  /// the server's own staleness window covers the same case, just later.
  Future<void> abandon(int paymentId) async {
    final response = await http.post(buildUri('api/Payment/$paymentId/abandon'), headers: authHeaders());
    decode(response);
  }
}
