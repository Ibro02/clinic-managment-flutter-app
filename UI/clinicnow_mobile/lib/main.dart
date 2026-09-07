import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/app_theme.dart';
import 'core/auth_session.dart';
import 'core/notification_center.dart';
import 'layouts/app_shell.dart';
import 'screens/login_screen.dart';

void main() {
  runApp(const ClinicNowMobileApp());
}

/// ClinicNow patient mobile app entry point. A single [AuthSession] instance
/// is provided app-wide so every screen/provider shares one source of truth
/// for the current login state (see core/auth_session.dart).
class ClinicNowMobileApp extends StatelessWidget {
  const ClinicNowMobileApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => AuthSession()),
        // Above MaterialApp on purpose: pushed routes (the notifications
        // screen) resolve providers from here, and the store has to outlive
        // any one screen for the badge to keep counting while the patient is
        // somewhere else in the app. It starts and stops itself off
        // AuthSession, so there is nothing to remember to call on login.
        ChangeNotifierProvider(
          create: (context) => NotificationCenter(context.read<AuthSession>()),
        ),
      ],
      child: MaterialApp(
        title: 'ClinicNow',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: ThemeMode.system,
        // Reactive root: whichever screen is shown follows AuthSession
        // directly, so logging in, explicit logout, AND an HTTP 401 clearing
        // the session mid-use (BaseProvider) all redirect correctly without
        // the route stack's *first* route needing to call Navigator itself
        // (rulebook §5/Appendix A.2 - expired tokens must redirect to
        // login). Screens pushed on top (e.g. RegisterScreen) still pop
        // themselves explicitly once they're done - see register_screen.dart.
        home: Consumer<AuthSession>(
          builder: (context, session, child) =>
              session.isLoggedIn ? const AppShell() : const LoginScreen(),
        ),
      ),
    );
  }
}
