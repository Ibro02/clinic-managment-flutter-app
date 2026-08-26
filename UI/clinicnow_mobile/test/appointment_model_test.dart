import 'dart:convert';

import 'package:clinicnow_mobile/models/appointment.dart';
import 'package:clinicnow_mobile/models/paged_result.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // Verbatim response body from `GET /api/Appointment` as served by the API the
  // mobile app actually talks to. The payment fields (isPaid/canRefund/
  // paymentStatus/paymentId) are absent - an older API build predating the
  // payments phase omits them, and any deployment lag reproduces this.
  const responseWithoutPaymentFields = '''
  {"resultList":[{"id":1,"patientId":1,"patientName":"Hana Pacijentić","doctorId":1,
  "doctorName":"Emir Doktorović","medicalServiceId":1,"medicalServiceName":"Opći pregled",
  "locationId":1,"locationName":"Poliklinika Centar","startUtc":"2026-08-18T09:00:00Z",
  "endUtc":"2026-08-18T09:30:00Z","status":2,"statusName":"Završen",
  "cancellationReason":null,"createdAtUtc":"2026-01-01T00:00:00Z","allowedActions":[]}],
  "count":1}
  ''';

  test('a payload without the payment fields still parses into the list', () {
    final json = jsonDecode(responseWithoutPaymentFields) as Map<String, dynamic>;

    final result = PagedResult<Appointment>.fromJson(json, Appointment.fromJson);

    expect(result.count, 1);
    expect(result.resultList, hasLength(1));
    expect(result.resultList.single.doctorName, 'Emir Doktorović');
    // Absent payment state degrades to "not paid / nothing to refund" rather
    // than blowing up the whole list.
    expect(result.resultList.single.isPaid, isFalse);
    expect(result.resultList.single.canRefund, isFalse);
    expect(result.resultList.single.paymentStatus, isNull);
    expect(result.resultList.single.paymentId, isNull);
  });
}
