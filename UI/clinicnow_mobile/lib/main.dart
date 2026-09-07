import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/app_theme.dart';
import 'core/auth_session.dart';
import 'core/notification_center.dart';
import 'core/push_notifications.dart';
import 'layouts/app_shell.dart';
import 'screens/login_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  // Firebase is deliberately NOT initialised here. `Firebase.initializeApp()`
  // talks to Play Services, and on a cold or offline device it can hang rather
  // than throw - which, awaited before runApp(), leaves the user staring at a
  // blank screen with no error to show for it. Nothing about drawing this app
  // depends on push, so nothing about push may delay drawing it.
  //
  // PushNotifications initialises Firebase itself, after login and off the
  // startup path (the call is idempotent, so doing it there is safe).
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
        // Registers this device with the backend on login and follows FCM token
        // rotation. Provided (not created inside a screen) because it has to
        // outlive every screen and survive navigation - and because the shell's
        // logout needs to reach it to unregister before the session clears.
        Provider<PushNotifications>(
          // lazy: false is load-bearing. Provider builds on first read, and the
          // only place that reads this one is the shell's logout handler - so
          // by default the object was not constructed until the user signed
          // *out*, having never subscribed to AuthSession and never registered
          // the device. Unlike NotificationCenter, which a widget watches and
          // which therefore builds on its own, nothing watches this: it works
          // entirely through a listener, so it has to exist before the login
          // it is waiting for.
          lazy: false,
          create: (context) {
            final push = PushNotifications(context.read<AuthSession>());
            // A push that lands while the app is open draws no tray
            // notification, so the badge and list are refreshed instead.
            final notifications = context.read<NotificationCenter>();
            push.onForegroundMessage = notifications.refresh;
            return push;
          },
          dispose: (_, push) => push.dispose(),
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
