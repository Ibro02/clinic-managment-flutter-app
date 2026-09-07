import 'dart:async';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

import 'auth_api.dart';
import 'auth_session.dart';

/// Device push notifications, so a patient hears about their appointment even
/// with the app closed - the one thing neither SignalR nor the notification
/// poll can do, since both need the app to be running.
///
/// Deliberately thin. Firebase delivers the message and Android draws the
/// notification; this class owns only the part that is ours: asking permission,
/// telling the backend which device to send to, and keeping that registration
/// honest as the session and the token change.
///
/// Everything here is best-effort. A patient who declines the permission, or
/// whose device cannot reach Firebase, still gets every notification in-app -
/// push adds to that, never replaces it - so no failure in this file is allowed
/// to surface as an error.
class PushNotifications {
  PushNotifications(this._session) {
    _session.addListener(_onSessionChanged);
    _wasLoggedIn = _session.isLoggedIn;
    if (_wasLoggedIn) unawaited(_register());
  }

  final AuthSession _session;
  final _authApi = AuthApi();

  StreamSubscription<String>? _tokenRefreshes;
  StreamSubscription<RemoteMessage>? _foregroundMessages;

  /// The token currently registered with the backend, so [unregister] can
  /// remove exactly that one instead of re-asking Firebase mid-sign-out.
  String? _registeredToken;

  bool _wasLoggedIn = false;

  /// Called when a push arrives while the app is in the foreground. Android
  /// draws no tray notification in that case - the app is already on screen -
  /// so the badge and list refresh instead of an entry appearing behind the UI
  /// the user is looking at.
  VoidCallback? onForegroundMessage;

  void _onSessionChanged() {
    final isLoggedIn = _session.isLoggedIn;
    if (isLoggedIn == _wasLoggedIn) return;
    _wasLoggedIn = isLoggedIn;

    if (isLoggedIn) {
      unawaited(_register());
    } else {
      // The session is already cleared by the time this fires - on an explicit
      // logout the screen has called [unregister] first, and on an expired
      // token (BaseProvider's 401 path) there is no valid token left to
      // authorize the call with. Either way, only local teardown is possible.
      _cancelSubscriptions();
      _registeredToken = null;
    }
  }

  /// Removes this device from the account, called during sign-out **before**
  /// `AuthSession.clear()` - the caller passes the token because clearing the
  /// session is what makes the call impossible afterwards.
  ///
  /// Best-effort and never rethrows: a patient signing out must not be stopped
  /// by a failing notification-registration call.
  Future<void> unregister(String authToken) async {
    final deviceToken = _registeredToken;
    _cancelSubscriptions();
    _registeredToken = null;

    if (deviceToken == null) return;

    try {
      await _authApi.unregisterDevice(token: authToken, deviceToken: deviceToken);
    } catch (error) {
      debugPrint('Unregistering this device for push failed: $error');
    }
  }

  /// How long to wait for Play Services before giving up on push for this
  /// session. `Firebase.initializeApp()` can hang rather than throw on a cold
  /// or offline device, and an unbounded wait here would be a registration that
  /// never happens and never says why.
  static const _initTimeout = Duration(seconds: 20);

  /// The channel every ClinicNow notification is posted on, foreground and
  /// background alike.
  ///
  /// Declared rather than left to FCM's fallback: on Android 8+ the channel -
  /// not the payload - owns sound and importance, and the fallback channel
  /// shows up in the phone's notification settings as "Miscellaneous", which
  /// tells a patient nothing about what they would be switching off. `high`
  /// importance is what produces a heads-up banner and a sound.
  static const _channel = AndroidNotificationChannel(
    'clinicnow_notifications',
    'ClinicNow obavijesti',
    description: 'Obavijesti o terminima, uputnicama i plaćanjima.',
    importance: Importance.high,
  );

  final _localNotifications = FlutterLocalNotificationsPlugin();
  bool _localNotificationsReady = false;

  Future<void> _register() async {
    try {
      // Idempotent - returns the already-initialised default app on later
      // calls. Done here rather than in main() so a hang costs push, not the
      // app's first frame.
      await Firebase.initializeApp().timeout(_initTimeout);

      final messaging = FirebaseMessaging.instance;

      // Android 13+ shows the system prompt here; older versions grant it
      // implicitly. A refusal is a legitimate answer, not an error - stop
      // quietly rather than registering a device that can never display
      // anything.
      final settings = await messaging.requestPermission();
      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        return;
      }

      final token = await messaging.getToken();
      if (token == null) return;

      await _sendRegistration(token);

      // FCM rotates tokens (app restore, cleared storage, long inactivity).
      // Without this the backend keeps pushing to an address the device no
      // longer answers on, and notifications stop with nothing to show why.
      _tokenRefreshes?.cancel();
      _tokenRefreshes = messaging.onTokenRefresh.listen((refreshed) {
        unawaited(_sendRegistration(refreshed));
      });

      await _prepareLocalNotifications();

      _foregroundMessages?.cancel();
      _foregroundMessages = FirebaseMessaging.onMessage.listen((message) {
        // Android deliberately draws nothing for the app that is in front, so
        // a foreground push would otherwise be silent and invisible. Drawing it
        // here is what makes "notified at any time" true while the app is open,
        // and it goes to the same channel as the background one so the two look
        // and sound identical.
        unawaited(_showLocally(message));
        onForegroundMessage?.call();
      });
    } catch (error) {
      // No Firebase config, no Play Services, no network - all real on a
      // patient's phone, none of them worth interrupting them over.
      debugPrint('Push registration skipped: $error');
    }
  }

  /// Registers the channel and the plugin once per session. Creating the
  /// channel is idempotent on Android - it updates the existing one rather than
  /// duplicating it - so this is safe to re-run on a re-login.
  Future<void> _prepareLocalNotifications() async {
    if (_localNotificationsReady) return;

    try {
      await _localNotifications.initialize(
        // The launcher icon doubles as the status-bar icon. Android wants a
        // white-on-transparent silhouette here; the launcher icon is a
        // placeholder until ClinicNow has a dedicated notification asset.
        settings: const InitializationSettings(
          android: AndroidInitializationSettings('@mipmap/ic_launcher'),
        ),
      );

      await _localNotifications
          .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
          ?.createNotificationChannel(_channel);

      _localNotificationsReady = true;
    } catch (error) {
      debugPrint('Local notifications unavailable; foreground pushes will not be drawn: $error');
    }
  }

  Future<void> _showLocally(RemoteMessage message) async {
    final notification = message.notification;
    if (!_localNotificationsReady || notification == null) return;

    try {
      await _localNotifications.show(
        // Keyed to the server's notification id so the same notification
        // arriving twice replaces its own entry instead of stacking a
        // duplicate. Falls back to the message's own hash when absent.
        id: int.tryParse(message.data['notificationId'] as String? ?? '') ?? message.hashCode,
        title: notification.title,
        body: notification.body,
        notificationDetails: NotificationDetails(
          android: AndroidNotificationDetails(
            _channel.id,
            _channel.name,
            channelDescription: _channel.description,
            importance: Importance.high,
            priority: Priority.high,
          ),
        ),
      );
    } catch (error) {
      debugPrint('Drawing a foreground notification failed: $error');
    }
  }

  Future<void> _sendRegistration(String deviceToken) async {
    final authToken = _session.token;
    if (authToken == null) return;

    try {
      await _authApi.registerDevice(token: authToken, deviceToken: deviceToken, platform: 'Android');
      _registeredToken = deviceToken;
    } catch (error) {
      debugPrint('Registering this device for push failed: $error');
    }
  }

  void _cancelSubscriptions() {
    _tokenRefreshes?.cancel();
    _tokenRefreshes = null;
    _foregroundMessages?.cancel();
    _foregroundMessages = null;
  }

  void dispose() {
    _session.removeListener(_onSessionChanged);
    _cancelSubscriptions();
  }
}
