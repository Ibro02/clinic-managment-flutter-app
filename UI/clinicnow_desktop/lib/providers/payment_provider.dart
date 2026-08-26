import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/payment.dart';

class PaymentProvider extends BaseProvider<Payment> {
  PaymentProvider(AuthSession authSession) : super('Payment', authSession);

  @override
  Payment fromJson(Map<String, dynamic> json) => Payment.fromJson(json);

  Future<Payment> refund(int paymentId, double amount, String reason) async {
    final response = await http.post(
      buildUri('api/Payment/$paymentId/refund'),
      headers: authHeaders(),
      body: jsonEncode({'amount': amount, 'reason': reason}),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
