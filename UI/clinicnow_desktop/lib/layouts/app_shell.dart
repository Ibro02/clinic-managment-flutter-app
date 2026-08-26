import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/auth_api.dart';
import '../core/auth_session.dart';
import '../core/roles.dart';
import '../screens/appointments/appointment_screen.dart';
import '../screens/codebooks/codebooks_screen.dart';
import '../screens/dashboard/dashboard_screen.dart';
import '../screens/news/news_screen.dart';
import '../screens/people/doctor_screen.dart';
import '../screens/people/patient_screen.dart';
import '../widgets/notifications_bell.dart';

/// Post-login shell for the desktop (staff) app - a persistent side
/// navigation rail plus a content area, matching the reference repo's
/// `master_screen.dart` role. Real destinations (appointments, documents,
/// reports, ...) are added one at a time starting Phase 4.
///
/// Shown/hidden by `main.dart` reacting to `AuthSession.isLoggedIn` - this
/// widget itself never navigates to `LoginScreen` directly; clearing the
/// session (here, or automatically on an HTTP 401 anywhere in the app) is
/// enough to redirect back.
class AppShell extends StatefulWidget {
  const AppShell({super.key});

  @override
  State<AppShell> createState() => _AppShellState();
}

class _NavEntry {
  final NavigationRailDestination destination;
  final WidgetBuilder builder;

  const _NavEntry({required this.destination, required this.builder});
}

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;
  final _authApi = AuthApi();
  bool _isLoggingOut = false;

  Future<void> _logout(BuildContext context) async {
    final session = context.read<AuthSession>();
    final token = session.token;

    setState(() => _isLoggingOut = true);
    if (token != null) {
      await _authApi.logout(token); // best-effort; always clears locally after
    }
    session.clear();
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final roleLabel = authSession.roles.isNotEmpty ? authSession.roles.first : '';

    // Codebook/patient/doctor management is an Administrator/Staff concern;
    // Doctor accounts can log in to the desktop app and browse patients/
    // doctors (read-only there) but don't manage codebooks. The backend
    // independently enforces the same boundaries on every write endpoint
    // regardless of what the nav rail shows.
    final canManageCodebooks = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);
    // Same condition as canManageCodebooks today, named separately because the
    // two visibility rules (dashboard/reports vs. codebook CRUD) are
    // independent business decisions that happen to currently coincide.
    final canViewReports = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    final entries = <_NavEntry>[
      if (canViewReports)
        _NavEntry(
          destination: const NavigationRailDestination(
            icon: Icon(Icons.dashboard_outlined),
            selectedIcon: Icon(Icons.dashboard),
            label: Text('Početna'),
          ),
          builder: (_) => const DashboardScreen(),
        ),
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.people_outline),
          selectedIcon: Icon(Icons.people),
          label: Text('Pacijenti'),
        ),
        builder: (_) => const PatientScreen(),
      ),
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.medical_services_outlined),
          selectedIcon: Icon(Icons.medical_services),
          label: Text('Doktori'),
        ),
        builder: (_) => const DoctorScreen(),
      ),
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.event_outlined),
          selectedIcon: Icon(Icons.event),
          label: Text('Termini'),
        ),
        builder: (_) => const AppointmentScreen(),
      ),
      if (canManageCodebooks)
        _NavEntry(
          destination: const NavigationRailDestination(
            icon: Icon(Icons.list_alt_outlined),
            selectedIcon: Icon(Icons.list_alt),
            label: Text('Šifrarnici'),
          ),
          builder: (_) => const CodebooksScreen(),
        ),
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.campaign_outlined),
          selectedIcon: Icon(Icons.campaign),
          label: Text('Obavijesti'),
        ),
        builder: (_) => const NewsScreen(),
      ),
    ];

    // If a role change (re-login) shrinks the destination list, don't leave
    // _selectedIndex pointing past the end.
    final selectedIndex = _selectedIndex < entries.length ? _selectedIndex : 0;

    return Scaffold(
      body: Row(
        children: [
          NavigationRail(
            selectedIndex: selectedIndex,
            onDestinationSelected: (index) =>
                setState(() => _selectedIndex = index),
            labelType: NavigationRailLabelType.all,
            leading: Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Column(
                children: [
                  const Icon(Icons.local_hospital),
                  const SizedBox(height: 8),
                  Text(
                    authSession.fullName,
                    style: Theme.of(context).textTheme.bodySmall,
                    textAlign: TextAlign.center,
                  ),
                  Text(
                    roleLabel,
                    style: Theme.of(context).textTheme.labelSmall,
                  ),
                ],
              ),
            ),
            trailing: Expanded(
              child: Align(
                alignment: Alignment.bottomCenter,
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 16),
                  child: IconButton(
                    tooltip: 'Odjava',
                    icon: _isLoggingOut
                        ? const SizedBox(
                            height: 18,
                            width: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.logout),
                    onPressed: _isLoggingOut ? null : () => _logout(context),
                  ),
                ),
              ),
            ),
            destinations: entries.map((e) => e.destination).toList(),
          ),
          const VerticalDivider(width: 1),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Align(
                  alignment: Alignment.centerRight,
                  child: Padding(
                    padding: const EdgeInsets.only(top: 4, right: 8),
                    child: NotificationsBell(authSession: authSession),
                  ),
                ),
                Expanded(child: entries[selectedIndex].builder(context)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
