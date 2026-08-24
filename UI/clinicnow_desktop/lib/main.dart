import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/auth_session.dart';
import 'screens/login_screen.dart';

void main() {
  runApp(const ClinicNowDesktopApp());
}

/// ClinicNow staff desktop app entry point. A single [AuthSession] instance
/// is provided app-wide so every screen/provider shares one source of truth
/// for the current login state (see core/auth_session.dart).
class ClinicNowDesktopApp extends StatelessWidget {
  const ClinicNowDesktopApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (_) => AuthSession(),
      child: MaterialApp(
        title: 'ClinicNow',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: Colors.teal),
          useMaterial3: true,
        ),
        home: const LoginScreen(),
      ),
    );
  }
}
