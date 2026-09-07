import 'api_exception.dart';

/// The "why it happened and what to do now" half of a user-facing error.
///
/// Screens supply the outcome ("Termini nisu učitani") because only they know
/// what the person was trying to do; this supplies the cause and the next step,
/// which depend only on how the call failed. Keeping the split here is what
/// stops the app drifting back to `'Greška: $e'` - a string that tells a
/// patient neither what broke nor what to press.
///
/// Three cases, because they need three different actions from the user:
///   - the session died          -> sign in again (nothing they can retry)
///   - the server failed         -> our fault, wait and retry
///   - the request was rejected  -> the backend already wrote a specific,
///                                  actionable message; never paper over it
/// Anything else is a transport failure (no network, wrong host, DNS), which
/// is the one case where checking the connection is genuinely useful advice.
String failureCause(Object error) {
  if (error is ApiException) {
    if (error.isUnauthorized) {
      return 'Vaša prijava je istekla. Prijavite se ponovo da nastavite.';
    }
    if (error.statusCode >= 500) {
      return 'Došlo je do greške na našoj strani. Pokušajte ponovo za nekoliko trenutaka.';
    }
    return error.message;
  }

  return 'Ne možemo se povezati sa serverom klinike. '
      'Provjerite internet konekciju i pokušajte ponovo.';
}
