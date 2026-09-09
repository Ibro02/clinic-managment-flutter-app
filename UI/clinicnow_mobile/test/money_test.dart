import 'package:clinicnow_mobile/core/money.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('quotes KM and the EUR sum PayPal will actually charge', () {
    // 45 KM at the server's fixed rate. Both halves use the Bosnian decimal
    // comma, so the booking prompt and the payment confirmation read alike.
    expect(formatPriceWithEur(45, 23.01), '45,00 KM (naplaćuje se 23,01 EUR preko PayPala)');
  });

  test('omits the EUR half when the API sent no converted amount', () {
    // An older API build omits payableAmountEur, which parses as 0. Quoting
    // "0,00 EUR" would be a confident lie about what PayPal charges; leaving
    // it out is merely incomplete.
    expect(formatPriceWithEur(45, 0), '45,00 KM');
  });

  test('formats with a decimal comma and two places', () {
    expect(formatMoney(6.5), '6,50');
  });
}
