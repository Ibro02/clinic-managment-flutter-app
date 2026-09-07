/// The shell's destinations, as a type rather than the label strings the
/// sidebar happens to render (rulebook Part II §D: no magic strings). A screen
/// inside the shell asks for one of these; the shell decides which index that
/// currently maps to, since the list depends on the signed-in user's role.
enum ShellDestination { home, patients, doctors, appointments, news, reports, codebooks }

/// Lets a screen move the user to another destination without knowing anything
/// about the sidebar - the dashboard's KPI cards drilling into the list they
/// summarise (review item C8).
///
/// A KPI that can't be opened is a dead end: the number tells someone something
/// is wrong and then leaves them to find the right screen themselves. [canOpen]
/// exists so a card whose destination this user cannot see (a Doctor account has
/// no Šifrarnici) renders as a plain tile rather than a click that does nothing.
class ShellNavigation {
  const ShellNavigation({required this.onOpen, required this.available});

  /// Supplied by the shell; call [open] rather than this, so the availability
  /// check can never be skipped by accident.
  final void Function(ShellDestination destination) onOpen;

  /// The destinations the signed-in role can actually reach.
  final Set<ShellDestination> available;

  bool canOpen(ShellDestination destination) => available.contains(destination);

  void open(ShellDestination destination) {
    if (canOpen(destination)) onOpen(destination);
  }
}
