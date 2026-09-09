/// Money formatting shared by every prompt that quotes a sum to the patient.
///
/// The clinic prices its catalogue in KM, but PayPal charges in EUR, so a
/// patient who only ever sees KM cannot reconcile the amount with the one that
/// shows up on their statement. Every prompt that names a price therefore names
/// both, in one place, so booking and paying can never disagree about the sum.
library;

/// Bosnian decimal comma, without pulling in locale data the app doesn't
/// otherwise initialise.
String formatMoney(double amount) => amount.toStringAsFixed(2).replaceAll('.', ',');

/// "45,00 KM (naplaćuje se 23,01 EUR preko PayPala)".
///
/// [payableAmountEur] is the server's own conversion - the exact sum PayPal
/// will charge - never a rate computed on the client. Zero means the API
/// didn't send one (an older build), and the EUR half is then left out rather
/// than invented: a wrong number here is worse than a missing one.
String formatPriceWithEur(double priceKm, double payableAmountEur) {
  final km = '${formatMoney(priceKm)} KM';
  if (payableAmountEur <= 0) return km;
  return '$km (naplaćuje se ${formatMoney(payableAmountEur)} EUR preko PayPala)';
}
