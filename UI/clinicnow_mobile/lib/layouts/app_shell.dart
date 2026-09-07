import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/auth_api.dart';
import '../core/auth_session.dart';
import '../core/notification_center.dart';
import '../core/push_notifications.dart';
import '../screens/appointments/my_appointments_screen.dart';
import '../screens/documents/my_documents_screen.dart';
import '../screens/news/news_list_screen.dart';
import '../screens/notifications/notifications_screen.dart';
import '../screens/profile/profile_screen.dart';
import '../screens/recommendations/recommendations_screen.dart';

/// Post-login shell for the mobile (patient) app - a bottom navigation bar
/// plus a content area. Real destinations (browse & book, "My appointments",
/// documents, profile, recommendations) are added starting Phase 1/4; this
/// establishes the shape so later phases only add `NavigationDestination`s
/// instead of restructuring the shell.
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

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;
  final _authApi = AuthApi();
  bool _isLoggingOut = false;

  Future<void> _logout(BuildContext context) async {
    final session = context.read<AuthSession>();
    final push = context.read<PushNotifications>();
    final token = session.token;

    setState(() => _isLoggingOut = true);
    if (token != null) {
      // Before the logout call and before clear(): both invalidate the token
      // this needs, and leaving the device registered would deliver this
      // patient's notifications to whoever signs in next.
      await push.unregister(token);
      await _authApi.logout(token); // best-effort; always clears locally after
    }
    session.clear();
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    // The badge follows the shared store's own polling (review item C15) - no
    // refresh call when the notifications screen pops, because the count was
    // never this widget's to maintain.
    final unreadCount = context.watch<NotificationCenter>().unreadCount;

    return Scaffold(
      appBar: AppBar(
        title: Text(authSession.fullName.isNotEmpty ? authSession.fullName : 'ClinicNow'),
        actions: [
          IconButton(
            tooltip: 'Obavijesti',
            onPressed: () => Navigator.of(context).push(MaterialPageRoute(
              builder: (_) => const NotificationsScreen(),
            )),
            icon: Badge(
              label: Text('$unreadCount'),
              isLabelVisible: unreadCount > 0,
              child: const Icon(Icons.notifications_outlined),
            ),
          ),
          IconButton(
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
        ],
      ),
      body: switch (_selectedIndex) {
        1 => const MyAppointmentsScreen(),
        2 => const MyDocumentsScreen(),
        3 => const RecommendationsScreen(),
        4 => const ProfileScreen(),
        _ => const NewsListScreen(),
      },
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
            icon: Icon(Icons.folder_outlined),
            selectedIcon: Icon(Icons.folder),
            label: 'Dokumenti',
          ),
          NavigationDestination(
            icon: Icon(Icons.recommend_outlined),
            selectedIcon: Icon(Icons.recommend),
            label: 'Preporuke',
          ),
          // Review item C7: the shell had no profile or settings destination
          // at all, so a patient could not correct their own name or phone
          // number, or turn off the reminder emails.
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
