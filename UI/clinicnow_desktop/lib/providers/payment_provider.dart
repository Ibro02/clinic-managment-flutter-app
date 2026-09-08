import '../core/api_http.dart';
import 'dart:convert';


import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/payment.dart';

class PaymentProvider extends BaseProvider<Payment> {
  PaymentProvider(AuthSession authSession) : super('Payment', authSession);

  @override
  Payment fromJson(Map<String, dynamic> json) => Payment.fromJson(json);

  /// The appointment list DTO carries only `paymentId`/`canRefund` - the real
  /// remaining refundable balance lives on the payment itself, so the refund
  /// dialog fetches it here rather than guessing client-side. Returns the
  /// backend's `GET api/Payment/by-appointment/{id}` (404 when the appointment
  /// has no non-Pending payment, surfaced as an [ApiException] like any other
  /// non-2xx response).
  Future<Payment> getByAppointmentId(int appointmentId) async {
    final response = await apiGet(
      buildUri('api/Payment/by-appointment/$appointmentId'),
      headers: authHeaders(),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }

  Future<Payment> refund(int paymentId, double amount, String reason) async {
    final response = await apiPost(
      buildUri('api/Payment/$paymentId/refund'),
      headers: authHeaders(),
      body: jsonEncode({'amount': amount, 'reason': reason}),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
