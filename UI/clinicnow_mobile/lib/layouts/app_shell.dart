import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/auth_session.dart';
import '../screens/login_screen.dart';

/// Post-login shell for the mobile (patient) app - a bottom navigation bar
/// plus a content area, matching the mobile mockups in the ClinicNow spec
/// (home, schedule/appointments, patients-equivalent, reports are the
/// desktop's rail; here it's home/appointments/profile). Real destinations
/// (browse & book, "My appointments", documents, profile, recommendations)
/// are added starting Phase 1/4; this establishes the shape so later phases
/// only add `NavigationDestination`s instead of restructuring the shell.
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
      appBar: AppBar(
        title: Text(authSession.username ?? 'ClinicNow'),
        actions: [
          IconButton(
            tooltip: 'Odjava',
            icon: const Icon(Icons.logout),
            onPressed: () => _logout(context),
          ),
        ],
      ),
      body: const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text(
            'ClinicNow — zakazivanje, moji termini i dokumentacija dolaze u narednim fazama.',
            textAlign: TextAlign.center,
          ),
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _selectedIndex,
        onDestinationSelected: (index) =>
            setState(() => _selectedIndex = index),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Početna',
          ),
          NavigationDestination(
            icon: Icon(Icons.event_available_outlined),
            selectedIcon: Icon(Icons.event_available),
            label: 'Termini',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profil',
          ),
        ],
      ),
    );
  }
}
