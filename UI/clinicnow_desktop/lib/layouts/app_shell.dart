import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/auth_session.dart';
import '../screens/login_screen.dart';

/// Post-login shell for the desktop (staff) app - a persistent side
/// navigation rail plus a content area, matching the reference repo's
/// `master_screen.dart` role. Real destinations (patients, doctors,
/// appointments, codebooks, reports, ...) are added one at a time starting
/// Phase 2; this establishes the shape so later phases only add
/// `NavigationRailDestination`s instead of restructuring the shell.
class AppShell extends StatefulWidget {
  const AppShell({super.key});

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;

  void _logout(BuildContext context) {
    context.read<AuthSession>().clear();
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(builder: (_) => const LoginScreen()),
      (route) => false,
    );
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();

    return Scaffold(
      body: Row(
        children: [
          NavigationRail(
            selectedIndex: _selectedIndex,
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
                    authSession.username ?? '',
                    style: Theme.of(context).textTheme.bodySmall,
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
                    icon: const Icon(Icons.logout),
                    onPressed: () => _logout(context),
                  ),
                ),
              ),
            ),
            destinations: const [
              NavigationRailDestination(
                icon: Icon(Icons.dashboard_outlined),
                selectedIcon: Icon(Icons.dashboard),
                label: Text('Početna'),
              ),
            ],
          ),
          const VerticalDivider(width: 1),
          const Expanded(
            child: Center(
              child: Text(
                'ClinicNow — dashboard i moduli dolaze u narednim fazama.',
              ),
            ),
          ),
        ],
      ),
    );
  }
}
