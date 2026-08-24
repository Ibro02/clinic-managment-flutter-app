/// Represents a structured error response from the ClinicNow API.
///
/// Mirrors the shape produced by `ClinicNow.API.Filters.ExceptionFilter`:
/// `{ "errors": { "field": ["message"] } }`. Carrying the parsed field errors
/// lets screens show backend validation messages under the right form
/// control instead of a generic dialog (rulebook Appendix A.2:
/// "_handleResponse ne smije prikrivati backend validacijske poruke, vec ih
/// treba proslijediti korisniku").
class ApiException implements Exception {
  final int statusCode;
  final String message;
  final Map<String, List<String>> fieldErrors;

  ApiException({
    required this.statusCode,
    required this.message,
    this.fieldErrors = const {},
  });

  /// True for HTTP 401 - callers should redirect to login rather than
  /// showing this as a normal in-form error (rulebook Appendix A.2:
  /// "istekle tokene ne treba ignorisati").
  bool get isUnauthorized => statusCode == 401;

  @override
  String toString() => message;
}
